using System.Text.Json;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class TradesEndpointTests : IntegrationTestBase
{
    public TradesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
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

    private async Task<Guid> CreateSecurityAsync(HttpClient client, string symbol, string exchange = "TSX")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol,
            exchange,
            name = $"{symbol} fund",
            displayPrecision = 4,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_Buy_RoundTrip_DetailsAndLinkedPrice()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "RRSP");
        var veqt = await CreateSecurityAsync(client, "VEQT");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account,
            side = "Buy",
            securityId = veqt,
            fundingCurrencyId = cad,
            quantity = 2m,
            unitPrice = 50m,
            occurredAt = "2026-04-23T12:00:00Z",
            memo = "Buy VEQT",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("InvestmentBuy", body.GetProperty("shape").GetString());

        var details = body.GetProperty("details");
        Assert.Equal("trade", details.GetProperty("kind").GetString());
        Assert.Equal(account, details.GetProperty("accountId").GetGuid());
        Assert.Equal(veqt, details.GetProperty("securityId").GetGuid());
        Assert.Equal(cad, details.GetProperty("fundingCurrencyId").GetGuid());
        Assert.Equal(2m, details.GetProperty("quantity").GetDecimal());
        Assert.Equal(50m, details.GetProperty("unitPrice").GetDecimal());

        var id = body.GetProperty("id").GetGuid();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = await db.CommodityPrices.SingleAsync(p => p.CreatedByTransactionId == id, CT);
        Assert.Equal(veqt, price.CommodityId);
        Assert.Equal(cad, price.QuoteCurrencyId);
        Assert.Equal(50m, price.Rate);

        // GET round-trips with the same shape + details.
        var getResp = await client.GetAsync($"/api/v1/finances/transactions/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("InvestmentBuy", fetched.GetProperty("shape").GetString());
        Assert.Equal(2m, fetched.GetProperty("details").GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task Create_Sell_RoundTrip_ShapeAndSigns()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "Sell-RRSP");
        var veqt = await CreateSecurityAsync(client, "XEQT");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account,
            side = "Sell",
            securityId = veqt,
            fundingCurrencyId = cad,
            quantity = 2m,
            unitPrice = 50m,
            occurredAt = "2026-04-23T12:00:00Z",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("InvestmentSell", body.GetProperty("shape").GetString());
        var id = body.GetProperty("id").GetGuid();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits
            .Where(s => s.TransactionId == id).ToListAsync(CT);
        var cash = splits.Single(s => s.CommodityId == cad);
        var sec = splits.Single(s => s.CommodityId == veqt);
        Assert.Equal(100m, cash.Quantity);   // Sell: cash in
        Assert.Equal(-2m, sec.Quantity);      // units out
    }

    [Fact]
    public async Task Patch_Trade_MovesLinkedPriceRow()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "Patch-RRSP");
        var veqt = await CreateSecurityAsync(client, "PEQT");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account,
            side = "Buy",
            securityId = veqt,
            fundingCurrencyId = cad,
            quantity = 2m,
            unitPrice = 50m,
            occurredAt = "2026-04-23T12:00:00Z",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = created.GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/trades/{id}", new
            {
                accountId = account,
                side = "Buy",
                securityId = veqt,
                fundingCurrencyId = cad,
                quantity = 3m,
                unitPrice = 60m,
                occurredAt = "2026-05-01T12:00:00Z",
            }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = await db.CommodityPrices.SingleAsync(p => p.CreatedByTransactionId == id, CT);
        Assert.Equal(60m, price.Rate);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), price.PriceAt);
    }

    [Fact]
    public async Task Patch_Trade_PriceRowCollision_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "Collide-RRSP");
        var veqt = await CreateSecurityAsync(client, "CEQT");

        // Buy 1 at 2026-04-23 → price row for (CEQT, CAD) at that date.
        await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 1m, unitPrice = 40m, occurredAt = "2026-04-23T12:00:00Z",
        }, CT);

        // Buy 2 at a different date → its own price row.
        var buy2Resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 1m, unitPrice = 45m, occurredAt = "2026-05-01T12:00:00Z",
        }, CT);
        var buy2 = await buy2Resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var buy2Id = buy2.GetProperty("id").GetGuid();

        // Patch buy 2 onto buy 1's date → (CEQT, CAD) price collision.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/trades/{buy2Id}", new
            {
                accountId = account, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
                quantity = 1m, unitPrice = 45m, occurredAt = "2026-04-23T12:00:00Z",
            }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task Delete_Trade_RemovesTransactionAndCascadesPrice()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "Del-RRSP");
        var veqt = await CreateSecurityAsync(client, "DEQT");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 2m, unitPrice = 50m, occurredAt = "2026-04-23T12:00:00Z",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = created.GetProperty("id").GetGuid();

        var deleteResp = await client.DeleteAsync($"/api/v1/finances/transactions/{id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == id, CT));
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == id, CT));
    }

    [Fact]
    public async Task Create_Trade_OnNonInvestmentAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var veqt = await CreateSecurityAsync(client, "NEQT");

        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            kind = "Cash",
            reportingCurrencyId = cad,
        }, CT);
        var cash = await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = cash!.Id, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 2m, unitPrice = 50m, occurredAt = "2026-04-23T12:00:00Z",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_TradeViaTransferEditor_Returns400_AndLeavesTradeIntact()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var investment = await CreateInvestmentAccountAsync(client, cad, "Guard-RRSP");
        var veqt = await CreateSecurityAsync(client, "GEQT");

        // A second user account to serve as the bogus transfer destination.
        var destResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Guard-Dest", kind = "Cash", reportingCurrencyId = cad,
        }, CT);
        var dest = await destResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var tradeResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = investment, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 2m, unitPrice = 50m, occurredAt = "2026-04-23T12:00:00Z",
        }, CT);
        var trade = await tradeResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var tradeId = trade.GetProperty("id").GetGuid();

        // The transfer editor must refuse a trade id (shape is immutable; only delete is shape-agnostic).
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/transfers/{tradeId}", new
            {
                sourceAccountId = investment, destinationAccountId = dest!.Id, sourceAmount = 10m,
                date = "2026-04-23",
            }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);

        // The trade row is untouched: still InvestmentBuy with its annotation intact.
        var getResp = await client.GetAsync($"/api/v1/finances/transactions/{tradeId}", CT);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("InvestmentBuy", fetched.GetProperty("shape").GetString());
        Assert.Equal(2m, fetched.GetProperty("details").GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task Create_Trade_OutOfRangeSide_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var investment = await CreateInvestmentAccountAsync(client, cad, "Side-RRSP");
        var veqt = await CreateSecurityAsync(client, "SEQT");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = investment, side = 99, securityId = veqt, fundingCurrencyId = cad,
            quantity = 2m, unitPrice = 50m, occurredAt = "2026-04-23T12:00:00Z",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Trade_AfterBalanceEntry_RefiresReconciliation()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "Recon-RRSP");
        var veqt = await CreateSecurityAsync(client, "REQT");

        // Assert CAD cash = 1000 on 2026-05-31 (prior = 0, delta = 1000).
        var beResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{account}/balance-entries", new
            {
                commodityId = cad,
                assertedAmount = 1000m,
                date = "2026-05-31",
            }, CT);
        beResp.EnsureSuccessStatusCode();
        var be = await beResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var beId = be.GetProperty("transactionId").GetGuid();

        // Buy spending 100 CAD on 2026-05-01 (before the BE). This shifts the BE's prior to -100,
        // so reconciliation must bump the BE's account-leg delta to 1100.
        var buyResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/trades", new
        {
            accountId = account, side = "Buy", securityId = veqt, fundingCurrencyId = cad,
            quantity = 2m, unitPrice = 50m, occurredAt = "2026-05-01T12:00:00Z",
        }, CT);
        Assert.Equal(HttpStatusCode.Created, buyResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beAccountSplit = await db.AccountTransactionSplits
            .SingleAsync(s => s.TransactionId == beId && s.AccountId == account, CT);
        Assert.Equal(1100m, beAccountSplit.Quantity);
    }
}
