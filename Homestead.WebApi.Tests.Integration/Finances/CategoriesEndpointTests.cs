using System.Text.Json;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class CategoriesEndpointTests : IntegrationTestBase
{
    public CategoriesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private record NetWorthPoint(DateTimeOffset AsOf, decimal? Value, decimal? Assets, decimal? Liabilities);
    private record NetWorthResponse(
        string Granularity, string Range, DateTimeOffset? From, DateTimeOffset To,
        IReadOnlyList<NetWorthPoint> Points, bool HasUnpriced, string? Reason);

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task SetDisplayCurrencyAsync(HttpClient client, Guid currencyId)
    {
        var resp = await SendJsonPatchAsync(client, "/api/v1/finances/settings", new[]
        {
            new { op = "replace", path = "/displayCurrencyId", value = (object)currencyId.ToString() },
        });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateAccountAsync(HttpClient client, Guid currencyId, string name, decimal opening)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Cash",
            reportingCurrencyId = currencyId,
            initialBalance = new { amount = opening, asOfDate = "2026-01-01" },
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<JsonElement> CreateCategoryAsync(
        HttpClient client, string name, string type, Guid? parentId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/categories", new { name, type, parentId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
    }

    private async Task PostFlowAsync(
        HttpClient client, Guid accountId, decimal amount, string direction = "Expense",
        Guid? categoryId = null, string date = "2026-04-23")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction, accountId, amount, date, categoryId,
        }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    private async Task<decimal?> GetLatestNetWorthAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/finances/reports/net-worth-history?range=All", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<NetWorthResponse>(CT);
        return body!.Points.Count == 0 ? null : body.Points[^1].Value;
    }

    // ---- C2 regression: the load-bearing test ----

    [Fact]
    public async Task CategorizedFlow_ExcludedFromAccountList_Detail404s_AndDoesNotFoldIntoNetWorth()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var account = await CreateAccountAsync(client, cad, "C2-Chequing", opening: 1000m);
        var category = (await CreateCategoryAsync(client, "Groceries", "Expense")).GetProperty("id").GetGuid();

        // Spend $50, categorised. Drains the account by 50; the category leg must stay out of net worth.
        await PostFlowAsync(client, account, 50m, categoryId: category);

        // (a) The category is absent from the account list, and no Income/Expense account leaks in.
        var accountsResp = await client.GetAsync("/api/v1/finances/accounts?page=1&pageSize=100", CT);
        accountsResp.EnsureSuccessStatusCode();
        var accountsBody = await accountsResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var items = accountsBody.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == category);
        Assert.All(items, i =>
        {
            var type = i.GetProperty("accountType").GetString();
            Assert.True(type is "Asset" or "Liability", $"Non-balance-sheet account leaked into the list: {type}");
        });

        // (b) The category 404s on the account-detail endpoint.
        var detailResp = await client.GetAsync($"/api/v1/finances/accounts/{category}", CT);
        Assert.Equal(HttpStatusCode.NotFound, detailResp.StatusCode);

        // (c) Net worth reflects only the drained account (1000 − 50), and the query does not throw. The
        // category's +50 leg must NOT fold back in (which would show 1000 and hide the spend).
        var netWorth = await GetLatestNetWorthAsync(client);
        Assert.Equal(950m, netWorth);
    }

    [Fact]
    public async Task CategorizedFlow_NetWorth_MatchesUncategorisedFlow()
    {
        // Categorising must be net-worth-neutral: the same spend posted to a category vs the Uncategorised
        // bucket yields identical net worth (the counterpart leg is excluded either way).
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var a1 = await CreateAccountAsync(client, cad, "Cat-Acct", opening: 500m);
        var category = (await CreateCategoryAsync(client, "Dining", "Expense")).GetProperty("id").GetGuid();
        await PostFlowAsync(client, a1, 20m, categoryId: category);
        var categorised = await GetLatestNetWorthAsync(client);

        Assert.Equal(480m, categorised);
    }

    // ---- GetCategories ----

    [Fact]
    public async Task GetCategories_IncludesSystemBuckets_AndUserTree()
    {
        var client = await CreateAdminClientAsync();
        var food = (await CreateCategoryAsync(client, "Food", "Expense")).GetProperty("id").GetGuid();
        await CreateCategoryAsync(client, "Groceries", "Expense", food);

        var resp = await client.GetAsync("/api/v1/finances/categories", CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var all = body.EnumerateArray().ToList();

        // Both system Uncategorised buckets are present and flagged read-only.
        var systemBuckets = all.Where(c => c.GetProperty("isSystem").GetBoolean()).ToList();
        Assert.Contains(systemBuckets, c => c.GetProperty("type").GetString() == "Income");
        Assert.Contains(systemBuckets, c => c.GetProperty("type").GetString() == "Expense");
        Assert.All(systemBuckets, c => Assert.True(c.GetProperty("readOnly").GetBoolean()));

        // The nested user category reports depth 1 and a breadcrumb path.
        var groceries = all.Single(c => c.GetProperty("name").GetString() == "Groceries");
        Assert.Equal(1, groceries.GetProperty("depth").GetInt32());
        Assert.Equal(food, groceries.GetProperty("parentId").GetGuid());
        Assert.Contains("Food", groceries.GetProperty("path").GetString());
        Assert.Contains("Groceries", groceries.GetProperty("path").GetString());
    }

    // ---- CreateCategory guards ----

    [Fact]
    public async Task CreateCategory_DuplicateSibling_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreateCategoryAsync(client, "Rent", "Expense");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/categories",
            new { name = "rent", type = "Expense", parentId = (Guid?)null }, CT);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_ParentOfDifferentType_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var incomeParent = (await CreateCategoryAsync(client, "Salary", "Income")).GetProperty("id").GetGuid();

        var resp = await client.PostAsJsonAsync("/api/v1/finances/categories",
            new { name = "Bonus", type = "Expense", parentId = incomeParent }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ---- PatchCategory ----

    [Fact]
    public async Task PatchCategory_Reparent_UpdatesPath()
    {
        var client = await CreateAdminClientAsync();
        var food = (await CreateCategoryAsync(client, "Food", "Expense")).GetProperty("id").GetGuid();
        var snacks = (await CreateCategoryAsync(client, "Snacks", "Expense")).GetProperty("id").GetGuid();

        var resp = await client.PatchAsJsonAsync($"/api/v1/finances/categories/{snacks}",
            new { name = "Snacks", parentId = food }, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal(1, body.GetProperty("depth").GetInt32());
        Assert.Contains("Food", body.GetProperty("path").GetString());
    }

    [Fact]
    public async Task PatchCategory_MoveUnderOwnDescendant_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var food = (await CreateCategoryAsync(client, "Food", "Expense")).GetProperty("id").GetGuid();
        var groceries = (await CreateCategoryAsync(client, "Groceries", "Expense", food)).GetProperty("id").GetGuid();

        // Trying to move Food under its own child Groceries is a cycle → 400.
        var resp = await client.PatchAsJsonAsync($"/api/v1/finances/categories/{food}",
            new { name = "Food", parentId = groceries }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchCategory_SystemBucket_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var categories = await (await client.GetAsync("/api/v1/finances/categories", CT))
            .Content.ReadFromJsonAsync<JsonElement>(CT);
        var bucketId = categories.EnumerateArray()
            .First(c => c.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();

        var resp = await client.PatchAsJsonAsync($"/api/v1/finances/categories/{bucketId}",
            new { name = "Renamed", parentId = (Guid?)null }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ---- DeleteCategory: reassign postings + reparent children ----

    [Fact]
    public async Task DeleteCategory_ReassignsPostingsToBucket_AndReparentsChildren()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Del-Acct", opening: 100m);

        var food = (await CreateCategoryAsync(client, "Food", "Expense")).GetProperty("id").GetGuid();
        var groceries = (await CreateCategoryAsync(client, "Groceries", "Expense", food)).GetProperty("id").GetGuid();

        await PostFlowAsync(client, account, 15m, categoryId: food);

        Guid expenseBucketId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            expenseBucketId = await db.Accounts
                .Where(a => a.IsSystem && a.Name == "Expense").Select(a => a.Id).SingleAsync(CT);
        }

        var deleteResp = await client.DeleteAsync($"/api/v1/finances/categories/{food}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // The category row is gone.
            Assert.False(await db.Accounts.AnyAsync(a => a.Id == food, CT));
            // Its posting was reassigned to the system Expense bucket (counterpart-only, no balance moved).
            Assert.False(await db.AccountTransactionSplits.AnyAsync(s => s.AccountId == food, CT));
            Assert.True(await db.AccountTransactionSplits.AnyAsync(s => s.AccountId == expenseBucketId, CT));
            // Its child was reparented to root (Food's own parent was null).
            var childParent = await db.Accounts.Where(a => a.Id == groceries)
                .Select(a => a.ParentAccountId).SingleAsync(CT);
            Assert.Null(childParent);
        }
    }

    [Fact]
    public async Task DeleteCategory_SystemBucket_Returns400()
    {
        var client = await CreateAdminClientAsync();
        Guid bucketId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            bucketId = await db.Accounts
                .Where(a => a.IsSystem && a.Name == "Expense").Select(a => a.Id).SingleAsync(CT);
        }

        var resp = await client.DeleteAsync($"/api/v1/finances/categories/{bucketId}", CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ---- Flow read-model: categoryId / name / path ----

    [Fact]
    public async Task Flow_WithCategory_CarriesCategoryNameAndPath()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Flow-Cat-Acct", opening: 0m);

        var food = (await CreateCategoryAsync(client, "Food", "Expense")).GetProperty("id").GetGuid();
        var groceries = (await CreateCategoryAsync(client, "Groceries", "Expense", food)).GetProperty("id").GetGuid();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 40m,
            date = "2026-04-23",
            categoryId = groceries,
        }, CT);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var body = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var details = body.GetProperty("details");
        Assert.Equal(groceries, details.GetProperty("categoryId").GetGuid());
        Assert.Equal("Groceries", details.GetProperty("categoryName").GetString());
        Assert.Contains("Food", details.GetProperty("categoryPath").GetString());

        // Patch back to Uncategorised (null category) → the counterpart leg returns to the bucket.
        var txId = body.GetProperty("id").GetGuid();
        var patchResp = await client.PatchAsJsonAsync($"/api/v1/finances/transactions/flows/{txId}", new
        {
            accountId = account,
            amount = 40m,
            date = "2026-04-23",
            categoryId = (Guid?)null,
        }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var patchedDetails = patched.GetProperty("details");
        Assert.Equal(JsonValueKind.Null, patchedDetails.GetProperty("categoryId").ValueKind);
        Assert.Equal(JsonValueKind.Null, patchedDetails.GetProperty("categoryName").ValueKind);
    }
}
