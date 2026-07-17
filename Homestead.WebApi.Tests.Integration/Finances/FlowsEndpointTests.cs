using System.Text.Json;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class FlowsEndpointTests : IntegrationTestBase
{
    public FlowsEndpointTests(PostgresFixture fixture) : base(fixture) { }

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

    private async Task<Guid> CreateAccountAsync(HttpClient client, Guid currencyId, string name,
        string kind = "Cash")
    {
        // Non-Investment kinds require an initialBalance; Investment kinds require it omitted.
        var resp = kind == "Investment"
            ? await client.PostAsJsonAsync("/api/v1/finances/accounts", new
            {
                name,
                kind,
                reportingCurrencyId = currencyId,
            }, CT)
            : await client.PostAsJsonAsync("/api/v1/finances/accounts", new
            {
                name,
                kind,
                reportingCurrencyId = currencyId,
                initialBalance = new { amount = 0m, asOfDate = "2026-01-01" },
            }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    [Fact]
    public async Task Create_Expense_RoundTrip_PostsToExpense_WithFlowDetails()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var expense = await GetSystemAccountIdAsync("Expense");
        var account = await CreateAccountAsync(client, cad, "Flow-Cash-Exp");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
            description = "Groceries",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Expense", body.GetProperty("shape").GetString());
        Assert.Equal("Groceries", body.GetProperty("memo").GetString());

        var details = body.GetProperty("details");
        Assert.Equal("flow", details.GetProperty("kind").GetString());
        Assert.Equal(account, details.GetProperty("accountId").GetGuid());
        Assert.Equal(cad, details.GetProperty("currencyId").GetGuid());
        Assert.Equal(50m, details.GetProperty("amount").GetDecimal());

        var id = body.GetProperty("id").GetGuid();
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits.Where(s => s.TransactionId == id).ToListAsync(CT);
        Assert.Equal(-50m, splits.Single(s => s.AccountId == account).Quantity);
        Assert.Equal(50m, splits.Single(s => s.AccountId == expense).Quantity);
    }

    [Fact]
    public async Task Create_Income_RoundTrip_PostsToIncome_WithFlowDetails()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var income = await GetSystemAccountIdAsync("Income");
        var account = await CreateAccountAsync(client, cad, "Flow-Cash-Inc");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Income",
            accountId = account,
            amount = 40m,
            date = "2026-04-23",
            description = "Refund",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Income", body.GetProperty("shape").GetString());
        var details = body.GetProperty("details");
        Assert.Equal("flow", details.GetProperty("kind").GetString());
        Assert.Equal(40m, details.GetProperty("amount").GetDecimal());

        var id = body.GetProperty("id").GetGuid();
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits.Where(s => s.TransactionId == id).ToListAsync(CT);
        Assert.Equal(40m, splits.Single(s => s.AccountId == account).Quantity);
        Assert.Equal(-40m, splits.Single(s => s.AccountId == income).Quantity);
    }

    [Fact]
    public async Task Create_Expense_OnCreditCard_ReadsAsCharge()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var card = await CreateAccountAsync(client, cad, "Flow-CC", kind: "CreditCard");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = card,
            amount = 50m,
            date = "2026-04-23",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = body.GetProperty("id").GetGuid();

        // Same stored sign as on an asset: the account leg is −a (no per-kind sign flip).
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits.Where(s => s.TransactionId == id).ToListAsync(CT);
        Assert.Equal(-50m, splits.Single(s => s.AccountId == card).Quantity);
    }

    [Fact]
    public async Task Patch_Expense_UpdatesAmountAndDescription()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var expense = await GetSystemAccountIdAsync("Expense");
        var account = await CreateAccountAsync(client, cad, "Flow-Cash-Patch");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
            description = "Groceries",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = created.GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/flows/{id}", new
            {
                accountId = account,
                amount = 65m,
                date = "2026-04-24",
                description = "Groceries + gas",
            }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Expense", patched.GetProperty("shape").GetString());
        Assert.Equal("Groceries + gas", patched.GetProperty("memo").GetString());

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits.Where(s => s.TransactionId == id).ToListAsync(CT);
        Assert.Equal(-65m, splits.Single(s => s.AccountId == account).Quantity);
        Assert.Equal(65m, splits.Single(s => s.AccountId == expense).Quantity);
    }

    [Fact]
    public async Task Delete_Expense_RemovesTransaction()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Flow-Cash-Del");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 25m,
            date = "2026-04-23",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = created.GetProperty("id").GetGuid();

        // Delete routes through the shape-agnostic /transactions delete (Normal → TransferService.DeleteAsync).
        var deleteResp = await client.DeleteAsync($"/api/v1/finances/transactions/{id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == id, CT));
    }

    [Fact]
    public async Task Create_Expense_OnInvestmentAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var investment = await CreateAccountAsync(client, cad, "Flow-RRSP", kind: "Investment");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = investment,
            amount = 10m,
            date = "2026-04-23",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ChainReconciliation_InsertingExpenseBeforeBalanceEntry_RewritesDelta()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateAccountAsync(client, cad, "Flow-Cash-Chain");

        // BE on 2026-02-28 asserts $100 (prior = 0 → delta 100).
        var beResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-02-28" }, CT);
        var be = await beResp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);

        // Insert a $30 expense dated BEFORE the balance entry — drains −30 from cash on 2026-01-15.
        var flowResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = cash,
            amount = 30m,
            date = "2026-01-15",
        }, CT);
        Assert.Equal(HttpStatusCode.Created, flowResp.StatusCode);

        // BE's prior is now −$30; to still assert $100 the delta must re-reconcile to $130.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beSplits = await db.AccountTransactionSplits
            .Where(s => s.TransactionId == be!.TransactionId)
            .ToListAsync(CT);
        Assert.Equal(130m, beSplits.Single(s => s.AccountId == cash).Quantity);
    }

    [Fact]
    public async Task GetTransactions_FilterByExpenseShape_ReturnsOnlyExpenses()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Flow-Cash-ShapeFilter");

        // One of each direction on the same account.
        var expenseResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
        }, CT);
        var expenseId = (await expenseResp.Content.ReadFromJsonAsync<JsonElement>(CT)).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Income",
            accountId = account,
            amount = 30m,
            date = "2026-04-23",
        }, CT);

        // Scope to this account so the assertion is deterministic across the shared DB.
        var resp = await client.GetAsync(
            $"/api/v1/finances/transactions?accountId={account}&shape=Expense&page=1&pageSize=25", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        // Before the fix, shape=Expense fell through to the unfiltered default and also returned the
        // Income flow (and the account's opening balance). The filter must return only the expense.
        Assert.All(items, i => Assert.Equal("Expense", i.GetProperty("shape").GetString()));
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == expenseId);
    }

    // ----- Payee threading -----

    private static async Task<Guid> CreatePayeeAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/payees", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>(CT)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_WithPayee_RoundTrips_AndCarriesPayeeNameOnReads()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Flow-Payee");
        var payee = await CreatePayeeAsync(client, "Costco");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
            payeeId = payee,
        }, CT);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = created.GetProperty("id").GetGuid();
        var createdDetails = created.GetProperty("details");
        Assert.Equal(payee, createdDetails.GetProperty("payeeId").GetGuid());
        Assert.Equal("Costco", createdDetails.GetProperty("payeeName").GetString());

        // GET single carries the payee name.
        var single = await (await client.GetAsync($"/api/v1/finances/transactions/{id}", CT))
            .Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Costco", single.GetProperty("details").GetProperty("payeeName").GetString());

        // Account activity list carries the payee name too.
        var list = await (await client.GetAsync(
                $"/api/v1/finances/accounts/{account}/transactions?page=1&pageSize=25", CT))
            .Content.ReadFromJsonAsync<JsonElement>(CT);
        var row = list.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("Costco", row.GetProperty("details").GetProperty("payeeName").GetString());
    }

    [Fact]
    public async Task Patch_ClearsPayee()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Flow-Payee-Clear");
        var payee = await CreatePayeeAsync(client, "Costco");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
            payeeId = payee,
        }, CT);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(CT)).GetProperty("id").GetGuid();

        // Patch without a payeeId (null) clears the attachment.
        var patchResp = await client.PatchAsJsonAsync($"/api/v1/finances/transactions/flows/{id}", new
        {
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
            payeeId = (Guid?)null,
        }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var details = patched.GetProperty("details");
        Assert.Equal(JsonValueKind.Null, details.GetProperty("payeeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("payeeName").ValueKind);
    }

    [Fact]
    public async Task Create_WithNonexistentPayee_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateAccountAsync(client, cad, "Flow-Payee-Bad");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount = 50m,
            date = "2026-04-23",
            payeeId = Guid.NewGuid(),
        }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private record BalanceEntryItem(
        Guid TransactionId, Guid AccountId, Guid CommodityId,
        decimal AssertedAmount, DateOnly Date, string Memo);
}
