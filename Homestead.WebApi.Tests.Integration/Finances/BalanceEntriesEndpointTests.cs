using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class BalanceEntriesEndpointTests : IntegrationTestBase
{
    public BalanceEntriesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> CreateCashAccountAsync(HttpClient client, Guid currencyId, string name,
        decimal initial = 0m)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Cash",
            reportingCurrencyId = currencyId,
            initialBalance = new { amount = initial, asOfDate = "2026-01-01" },
        }, CT);
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    [Fact]
    public async Task Create_FirstBalanceEntry_RecordsAssertion()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-1");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new
            {
                commodityId = cad,
                assertedAmount = 1500m,
                date = "2026-04-25",
                memo = "End-of-month statement",
            }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);
        Assert.Equal(1500m, body!.AssertedAmount);
        Assert.Equal(new DateOnly(2026, 4, 25), body.Date);
        Assert.Equal("End-of-month statement", body.Memo);
    }

    [Fact]
    public async Task Create_BlankMemo_DefaultsToBalanceAssertion()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-Default");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-04-25" }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);
        Assert.Equal("Balance assertion", body!.Memo);
    }

    [Fact]
    public async Task Create_DuplicateOnSameDate_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-2");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-04-25" }, CT);

        var dup = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 200m, date = "2026-04-25" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
    }

    [Fact]
    public async Task ChainReconciliation_InsertingTransferBetweenEntries_RewritesDeltas()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-Chain");
        var savings = await CreateCashAccountAsync(client, cad, "Savings-BE-Chain", 500m);

        // BE1 on 2026-01-31 asserts $100 (prior = 0 since the account opens at $0).
        var be1Resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-01-31" }, CT);
        var be1 = await be1Resp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);

        // BE2 on 2026-02-28 asserts $250 (prior = 100, delta = 150).
        var be2Resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 250m, date = "2026-02-28" }, CT);
        var be2 = await be2Resp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);

        // Insert a $50 transfer from savings → cash on 2026-01-15.
        var transferResp = await client.PostAsJsonAsync(
            "/api/v1/finances/transactions/transfers",
            new
            {
                sourceAccountId = savings,
                destinationAccountId = cash,
                sourceAmount = 50m,
                date = "2026-01-15",
            }, CT);
        Assert.Equal(HttpStatusCode.Created, transferResp.StatusCode);

        // BE1's prior is now $50 (from the transfer); delta = $50.
        // BE2's prior is now $50 + $50 (BE1 split) = $100; delta = $150 (unchanged).
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adjustments = await db.Accounts
            .SingleAsync(a => a.IsSystem && a.Name == SystemAccountSeeder.AdjustmentsName, CT);

        var be1Splits = await db.AccountTransactionSplits
            .Where(s => s.TransactionId == be1!.TransactionId)
            .ToListAsync(CT);
        var be1Account = be1Splits.Single(s => s.AccountId == cash);
        var be1Adj = be1Splits.Single(s => s.AccountId == adjustments.Id);
        Assert.Equal(50m, be1Account.Quantity);
        Assert.Equal(-50m, be1Adj.Quantity);

        var be2Splits = await db.AccountTransactionSplits
            .Where(s => s.TransactionId == be2!.TransactionId)
            .ToListAsync(CT);
        var be2Account = be2Splits.Single(s => s.AccountId == cash);
        Assert.Equal(150m, be2Account.Quantity);
    }

    [Fact]
    public async Task Patch_BalanceEntry_UpdatesAssertedAmountAndReconciles()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-Patch");

        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-04-25" }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries/{created!.TransactionId}",
            new { commodityId = cad, assertedAmount = 200m, date = "2026-04-25" }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var be = await db.AccountTransactions.OfType<BalanceEntryTransaction>()
            .Include(e => e.Splits)
            .SingleAsync(e => e.Id == created.TransactionId, CT);
        Assert.Equal(200m, be.AssertedQuantity);
        Assert.Equal(200m, be.Splits.Single(s => s.AccountId == cash).Quantity);
    }

    [Fact]
    public async Task Delete_BalanceEntry_RemovesAndReconcilesChain()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-Del");

        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-04-25" }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);

        var deleteResp = await client.DeleteAsync($"/api/v1/finances/transactions/{created!.TransactionId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == created.TransactionId, CT));
    }

    [Fact]
    public async Task GetList_ReturnsBalanceEntries_ForAccount()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var cash = await CreateCashAccountAsync(client, cad, "Cash-BE-List");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 100m, date = "2026-01-31" }, CT);
        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash}/balance-entries",
            new { commodityId = cad, assertedAmount = 200m, date = "2026-02-28" }, CT);

        var listResp = await client.GetAsync($"/api/v1/finances/accounts/{cash}/balance-entries", CT);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var items = await listResp.Content.ReadFromJsonAsync<List<BalanceEntryItem>>(CT);
        Assert.Equal(2, items!.Count);
    }

    private record BalanceEntryItem(
        Guid TransactionId, Guid AccountId, Guid CommodityId,
        decimal AssertedAmount, DateOnly Date, string Memo);
}
