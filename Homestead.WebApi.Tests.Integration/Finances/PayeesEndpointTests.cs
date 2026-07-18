using System.Text.Json;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

internal record PayeeItem(
    Guid Id, string Name, Guid? DefaultCategoryId, string? DefaultCategoryName,
    string? DefaultCategoryType, string? Notes, bool IsArchived, int TransactionCount,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

internal record MappingItem(
    Guid Id, string NormalizedKey, string SampleRawDescription, int TimesConfirmed, DateTimeOffset UpdatedAt);

[Collection("Integration")]
public class PayeesEndpointTests : IntegrationTestBase
{
    public PayeesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> GetSystemAccountIdAsync(string name)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Accounts.Where(a => a.IsSystem && a.Name == name).Select(a => a.Id).SingleAsync(CT);
    }

    private static async Task<PayeeItem> CreatePayeeAsync(
        HttpClient client, string name, Guid? defaultCategoryId = null, string? notes = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/payees",
            new { name, defaultCategoryId, notes }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PayeeItem>(CT))!;
    }

    private static async Task<Guid> CreateExpenseCategoryAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/categories",
            new { name, type = "Expense", parentId = (Guid?)null }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>(CT)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCashAccountAsync(HttpClient client, string name)
    {
        var cad = await GetCurrencyIdAsync("CAD");
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Cash",
            reportingCurrencyId = cad,
            initialBalance = new { amount = 0m, asOfDate = "2026-01-01" },
        }, CT);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!.Id;
    }

    private static async Task<Guid> CreateFlowWithPayeeAsync(HttpClient client, Guid accountId, Guid payeeId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId,
            amount = 20m,
            date = "2026-04-23",
            payeeId,
        }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>(CT)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedMappingAsync(Guid payeeId, string normalizedKey, string sampleRaw)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mapping = new PayeeMapping
        {
            Id = Guid.NewGuid(),
            NormalizedKey = normalizedKey,
            NormalizationVersion = PayeeDescriptionNormalizer.Version,
            PayeeId = payeeId,
            SampleRawDescription = sampleRaw,
            TimesConfirmed = 1,
        };
        db.PayeeMappings.Add(mapping);
        await db.SaveChangesAsync(CT);
        return mapping.Id;
    }

    // ----- Merge -----

    [Fact]
    public async Task MergePayee_ReassignsTransactionsAndMappings_RemovesSource()
    {
        var client = await CreateAdminClientAsync();
        var source = await CreatePayeeAsync(client, "Blue Bottle");
        var target = await CreatePayeeAsync(client, "Blue Bottle Coffee");
        var account = await CreateCashAccountAsync(client, "Merge-Cash");
        var flowId = await CreateFlowWithPayeeAsync(client, account, source.Id);
        var mappingId = await SeedMappingAsync(source.Id, "BLUE BOTTLE", "SQ *BLUE BOTTLE");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/payees/{source.Id}/merge", new { targetId = target.Id }, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var merged = (await resp.Content.ReadFromJsonAsync<PayeeItem>(CT))!;
        Assert.Equal(target.Id, merged.Id);
        Assert.Equal(1, merged.TransactionCount);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Source removed.
        Assert.False(await db.Payees.AsNoTracking().AnyAsync(p => p.Id == source.Id, CT));
        // The flow's payee re-pointed to target — verifies the TPH OfType<NormalTransaction> ExecuteUpdate
        // composes on real Postgres (the risk flagged in the plan).
        var flowPayee = await db.AccountTransactions.OfType<NormalTransaction>().AsNoTracking()
            .Where(t => t.Id == flowId).Select(t => t.PayeeId).SingleAsync(CT);
        Assert.Equal(target.Id, flowPayee);
        // The learned mapping re-pointed to target (survives the source's cascade delete).
        var mappingPayee = await db.PayeeMappings.AsNoTracking()
            .Where(m => m.Id == mappingId).Select(m => m.PayeeId).SingleAsync(CT);
        Assert.Equal(target.Id, mappingPayee);
    }

    [Fact]
    public async Task MergePayee_IntoItself_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var payee = await CreatePayeeAsync(client, "Costco");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/payees/{payee.Id}/merge", new { targetId = payee.Id }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Learned mappings management -----

    [Fact]
    public async Task PayeeMappings_ListAndDelete()
    {
        var client = await CreateAdminClientAsync();
        var payee = await CreatePayeeAsync(client, "Costco");
        var mappingId = await SeedMappingAsync(payee.Id, "COSTCO", "COSTCO #123");

        var list = await (await client.GetAsync($"/api/v1/finances/payees/{payee.Id}/mappings", CT))
            .Content.ReadFromJsonAsync<List<MappingItem>>(CT);
        Assert.Single(list!);
        Assert.Equal("COSTCO #123", list![0].SampleRawDescription);

        var del = await client.DeleteAsync($"/api/v1/finances/payee-mappings/{mappingId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var after = await (await client.GetAsync($"/api/v1/finances/payees/{payee.Id}/mappings", CT))
            .Content.ReadFromJsonAsync<List<MappingItem>>(CT);
        Assert.Empty(after!);
    }

    // ----- CRUD roundtrip -----

    [Fact]
    public async Task Crud_Roundtrip()
    {
        var client = await CreateAdminClientAsync();
        var category = await CreateExpenseCategoryAsync(client, "Groceries");

        var created = await CreatePayeeAsync(client, "Costco", category, notes: "Warehouse club");
        Assert.Equal("Costco", created.Name);
        Assert.Equal(category, created.DefaultCategoryId);
        Assert.Equal("Groceries", created.DefaultCategoryName);
        Assert.Equal("Expense", created.DefaultCategoryType);
        Assert.Equal("Warehouse club", created.Notes);
        Assert.False(created.IsArchived);
        Assert.Equal(0, created.TransactionCount);

        // Appears in the list.
        var list = await (await client.GetAsync("/api/v1/finances/payees?page=1&pageSize=50", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<PayeeItem>>(CT);
        Assert.Single(list!.Items, p => p.Id == created.Id);

        // Full-replace patch: rename, drop the default category, add notes, archive.
        var patchResp = await client.PatchAsJsonAsync($"/api/v1/finances/payees/{created.Id}", new
        {
            name = "Costco Wholesale",
            defaultCategoryId = (Guid?)null,
            notes = "Updated",
            isArchived = true,
        }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = (await patchResp.Content.ReadFromJsonAsync<PayeeItem>(CT))!;
        Assert.Equal("Costco Wholesale", patched.Name);
        Assert.Null(patched.DefaultCategoryId);
        Assert.Equal("Updated", patched.Notes);
        Assert.True(patched.IsArchived);
    }

    // ----- Duplicate name -----

    [Fact]
    public async Task CreatePayee_DuplicateCaseVariant_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreatePayeeAsync(client, "Costco");

        var dup = await client.PostAsJsonAsync("/api/v1/finances/payees", new { name = "  COSTCO " }, CT);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task PatchPayee_RenameOntoExistingName_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreatePayeeAsync(client, "Costco");
        var safeway = await CreatePayeeAsync(client, "Safeway");

        var resp = await client.PatchAsJsonAsync($"/api/v1/finances/payees/{safeway.Id}", new
        {
            name = "costco",
            isArchived = false,
        }, CT);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // ----- Delete -----

    [Fact]
    public async Task DeletePayee_Unused_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var payee = await CreatePayeeAsync(client, "Costco");

        var resp = await client.DeleteAsync($"/api/v1/finances/payees/{payee.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Payees.AsNoTracking().AnyAsync(p => p.Id == payee.Id, CT));
    }

    [Fact]
    public async Task DeletePayee_InUse_Returns409WithArchiveSuggestion_AndKeepsRow()
    {
        var client = await CreateAdminClientAsync();
        var payee = await CreatePayeeAsync(client, "Costco");
        var account = await CreateCashAccountAsync(client, "Payee-Del-Cash");
        await CreateFlowWithPayeeAsync(client, account, payee.Id);

        var resp = await client.DeleteAsync($"/api/v1/finances/payees/{payee.Id}", CT);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Contains("Archive", problem.GetProperty("detail").GetString());

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Payees.AsNoTracking().AnyAsync(p => p.Id == payee.Id, CT));
    }

    [Fact]
    public async Task DeletePayee_Missing_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.DeleteAsync($"/api/v1/finances/payees/{Guid.NewGuid()}", CT);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- IncludeArchived -----

    [Fact]
    public async Task GetPayees_ExcludesArchivedByDefault_IncludesWhenRequested()
    {
        var client = await CreateAdminClientAsync();
        await CreatePayeeAsync(client, "Costco");
        var old = await CreatePayeeAsync(client, "OldStore");
        await client.PatchAsJsonAsync($"/api/v1/finances/payees/{old.Id}",
            new { name = "OldStore", isArchived = true }, CT);

        var defaultList = await (await client.GetAsync("/api/v1/finances/payees?page=1&pageSize=50", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<PayeeItem>>(CT);
        Assert.DoesNotContain(defaultList!.Items, p => p.Id == old.Id);

        var withArchived = await (await client.GetAsync(
                "/api/v1/finances/payees?page=1&pageSize=50&includeArchived=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<PayeeItem>>(CT);
        Assert.Contains(withArchived!.Items, p => p.Id == old.Id);
    }

    // ----- Search + pagination + transactionCount -----

    [Fact]
    public async Task GetPayees_SearchAndPaginate()
    {
        var client = await CreateAdminClientAsync();
        await CreatePayeeAsync(client, "Costco");
        await CreatePayeeAsync(client, "Safeway");
        await CreatePayeeAsync(client, "Metro");

        var search = await (await client.GetAsync("/api/v1/finances/payees?page=1&pageSize=50&search=cost", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<PayeeItem>>(CT);
        Assert.Single(search!.Items);
        Assert.Equal("Costco", search.Items[0].Name);

        var paged = await (await client.GetAsync("/api/v1/finances/payees?page=1&pageSize=2", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<PayeeItem>>(CT);
        Assert.Equal(2, paged!.Items.Count);
        Assert.Equal(3, paged.TotalCount);
    }

    [Fact]
    public async Task GetPayees_PopulatesTransactionCount()
    {
        var client = await CreateAdminClientAsync();
        var payee = await CreatePayeeAsync(client, "Costco");
        var account = await CreateCashAccountAsync(client, "Payee-Count-Cash");
        await CreateFlowWithPayeeAsync(client, account, payee.Id);
        await CreateFlowWithPayeeAsync(client, account, payee.Id);

        var list = await (await client.GetAsync("/api/v1/finances/payees?page=1&pageSize=50", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<PayeeItem>>(CT);
        Assert.Equal(2, list!.Items.Single(p => p.Id == payee.Id).TransactionCount);
    }

    // ----- Default-category validation -----

    [Fact]
    public async Task CreatePayee_MissingDefaultCategory_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/finances/payees",
            new { name = "Costco", defaultCategoryId = Guid.NewGuid() }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreatePayee_NonCategoryDefaultCategory_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var assetAccount = await CreateCashAccountAsync(client, "Payee-Asset");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/payees",
            new { name = "Costco", defaultCategoryId = assetAccount }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreatePayee_SystemDefaultCategory_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var systemExpense = await GetSystemAccountIdAsync("Expense");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/payees",
            new { name = "Costco", defaultCategoryId = systemExpense }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Authorization -----

    [Fact]
    public async Task CreatePayee_NonContributor_Returns403()
    {
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync("/api/v1/finances/payees", new { name = "Costco" }, CT);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
