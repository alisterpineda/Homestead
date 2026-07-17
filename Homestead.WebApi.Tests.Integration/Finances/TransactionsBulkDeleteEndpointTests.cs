using System.Text.Json;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class TransactionsBulkDeleteEndpointTests : IntegrationTestBase
{
    public TransactionsBulkDeleteEndpointTests(PostgresFixture fixture) : base(fixture) { }

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

    private async Task<Guid> CreateInvestmentAccountAsync(HttpClient client, Guid currencyId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Investment",
            reportingCurrencyId = currencyId,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> CreateSecurityAsync(HttpClient client, string symbol)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol,
            exchange = "NYSEARCA",
            name = $"{symbol} fund",
            displayPrecision = 4,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTransferAsync(HttpClient client, Guid source, Guid destination,
        decimal amount, string date)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = source,
            destinationAccountId = destination,
            sourceAmount = amount,
            date,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TransactionItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> CreateExpenseAsync(HttpClient client, Guid account, decimal amount, string date)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/flows", new
        {
            direction = "Expense",
            accountId = account,
            amount,
            date,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBalanceEntryAsync(HttpClient client, Guid account, Guid commodity,
        decimal assertedAmount, string date)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{account}/balance-entries", new
            {
                commodityId = commodity,
                assertedAmount,
                date,
            }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<BalanceEntryItem>(CT);
        return body!.TransactionId;
    }

    private async Task<(Guid DividendId, Guid BuyId)> CreateDripAsync(HttpClient client, Guid account,
        Guid security, Guid currency)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips", new
        {
            accountId = account,
            securityId = security,
            paymentCurrencyId = currency,
            amount = 5m,
            units = 0.05m,
            occurredAt = "2026-04-23T12:00:00Z",
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("relatedTransactionId").GetGuid());
    }

    private static async Task<HttpResponseMessage> BulkDeleteAsync(HttpClient client, params Guid[] ids) =>
        await client.PostAsJsonAsync("/api/v1/finances/transactions/bulk-delete",
            new { ids }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task BulkDelete_MixedSet_DeletesAllAndReconcilesDownstreamBalanceEntry()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var chequing = await CreateCashAccountAsync(client, cad, "BulkMix-Chequing", 1000m);
        var savings = await CreateCashAccountAsync(client, cad, "BulkMix-Savings");

        var transferId = await CreateTransferAsync(client, chequing, savings, 200m, "2026-02-01");
        var flowId = await CreateExpenseAsync(client, chequing, 50m, "2026-02-10");
        // Running balance before BE1: 1000 - 200 - 50 = 750 → BE1 delta -50.
        var be1Id = await CreateBalanceEntryAsync(client, chequing, cad, 700m, "2026-03-01");
        // BE2 prior = 700 → delta +100. Survives the bulk delete.
        var be2Id = await CreateBalanceEntryAsync(client, chequing, cad, 800m, "2026-04-01");

        var resp = await BulkDeleteAsync(client, transferId, flowId, be1Id);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(3, body!.DeletedCount);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions
            .CountAsync(t => t.Id == transferId || t.Id == flowId || t.Id == be1Id, CT));

        // Only the opening balance (1000) remains before BE2, so its reconciled delta is 800 - 1000 = -200.
        var be2Split = await db.AccountTransactionSplits
            .SingleAsync(s => s.TransactionId == be2Id && s.AccountId == chequing, CT);
        Assert.Equal(-200m, be2Split.Quantity);
    }

    [Fact]
    public async Task BulkDelete_DripHalfId_DeletesBothHalvesAndLinkedPrice()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "BulkDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "BVFV");
        var (dividendId, buyId) = await CreateDripAsync(client, account, vfv, usd);

        // Only the Dividend half is requested; the Buy partner goes with it.
        var resp = await BulkDeleteAsync(client, dividendId);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == dividendId || t.Id == buyId, CT));
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == buyId, CT));
    }

    [Fact]
    public async Task BulkDelete_BothDripHalvesRequested_DeletesPairOnce_CountsTwo()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "BulkDrip2-RRSP");
        var vfv = await CreateSecurityAsync(client, "WVFV");
        var (dividendId, buyId) = await CreateDripAsync(client, account, vfv, usd);

        var resp = await BulkDeleteAsync(client, dividendId, buyId);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == dividendId || t.Id == buyId, CT));
    }

    [Fact]
    public async Task BulkDelete_OpeningBalanceId_Returns400AndDeletesNothing()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var chequing = await CreateCashAccountAsync(client, cad, "BulkOB-Chequing", 500m);
        var savings = await CreateCashAccountAsync(client, cad, "BulkOB-Savings");

        var obResp = await client.GetAsync($"/api/v1/finances/accounts/{chequing}/opening-balances", CT);
        var obs = await obResp.Content.ReadFromJsonAsync<List<OpeningBalanceItem>>(CT);
        var obId = obs!.Single().TransactionId;
        var transferId = await CreateTransferAsync(client, chequing, savings, 100m, "2026-02-01");

        var resp = await BulkDeleteAsync(client, obId, transferId);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        // All-or-nothing: the transfer survives too, even though it is individually deletable.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.AccountTransactions.CountAsync(t => t.Id == obId || t.Id == transferId, CT));
    }

    [Fact]
    public async Task BulkDelete_EmptyIds_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await BulkDeleteAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_NonexistentIds_ReturnsZeroDeleted()
    {
        var client = await CreateAdminClientAsync();

        var resp = await BulkDeleteAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(0, body!.DeletedCount);
    }

    [Fact]
    public async Task BulkDelete_TradeId_CascadesLinkedCommodityPrice()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "BulkTrade-RRSP");
        var veqt = await CreateSecurityAsync(client, "BEQT");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 2m, unitPrice = 50m, occurredAt = "2026-04-23T12:00:00Z",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var tradeId = created.GetProperty("id").GetGuid();

        var resp = await BulkDeleteAsync(client, tradeId);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(1, body!.DeletedCount);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == tradeId, CT));
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == tradeId, CT));
    }

    private record BalanceEntryItem(
        Guid TransactionId, Guid AccountId, Guid CommodityId,
        decimal AssertedAmount, DateOnly Date, string Memo);

    private record OpeningBalanceItem(
        Guid TransactionId, Guid AccountId, Guid CommodityId, string Memo,
        decimal Quantity, decimal? ValuePerUnit, DateOnly AsOfDate);
}
