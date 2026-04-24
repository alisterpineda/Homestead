using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class OpeningBalancesEndpointTests : IntegrationTestBase
{
    public OpeningBalancesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetUsdIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> CreateInvestmentAccountAsync(HttpClient client, Guid usdId, string name = "RRSP")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Investment",
            reportingCurrencyId = usdId,
        }, CT);
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Commodity> AddSecurityAsync(string symbol = "VEQT", string exchange = "TSX")
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Commodities
            .FirstOrDefaultAsync(c => c.Symbol == symbol && c.Exchange == exchange, CT);
        if (existing is not null) return existing;

        var security = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = symbol,
            Exchange = exchange,
            Name = symbol,
            Kind = CommodityKind.Security,
            DisplayPrecision = 4,
        };
        db.Commodities.Add(security);
        await db.SaveChangesAsync(CT);
        return security;
    }

    [Fact]
    public async Task AddCashPosition_ToInvestment_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new
            {
                commodityId = usdId,
                quantity = 5000m,
                asOfDate = "2026-04-23",
            }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(5000m, body!.Quantity);
        Assert.Equal(5000m, body.Value);
        Assert.Null(body.ValuePerUnit);
    }

    [Fact]
    public async Task AddSecurityPosition_EmitsPriceAndBalancesInReportingCurrency()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Brokerage");
        var veqt = await AddSecurityAsync("VEQT");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new
            {
                commodityId = veqt.Id,
                quantity = 100m,
                valuePerUnit = 35.12m,
                asOfDate = "2026-04-23",
            }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(100m, body!.Quantity);
        Assert.Equal(3512m, body.Value);
        Assert.Equal(35.12m, body.ValuePerUnit);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = await db.CommodityPrices.SingleAsync(p =>
            p.CommodityId == veqt.Id && p.QuoteCurrencyId == usdId, CT);
        Assert.Equal(35.12m, price.Rate);
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), price.PriceAt);
        Assert.NotNull(price.CreatedByTransactionId);
        Assert.Equal(body.TransactionId, price.CreatedByTransactionId);
    }

    [Fact]
    public async Task DuplicateAdd_ForSameAccountCommodity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Dup-acct");

        var first = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 500m, asOfDate = "2026-04-24" }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task AddToNonInvestmentAccount_WithForeignCommodity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var cash = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-USD",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 0m, asOfDate = "2026-04-23" },
        }, CT);
        var cashBody = await cash.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        // Set up a different currency commodity.
        Commodity cad;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cad = await db.Commodities.FirstAsync(c => c.Symbol == "CAD", CT);
        }

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cashBody!.Id}/opening-balances",
            new { commodityId = cad.Id, quantity = 500m, asOfDate = "2026-04-23" }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetList_ReturnsAllPositionsForAccount()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Multi-acct");
        var veqt = await AddSecurityAsync("VEQT-LIST");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);
        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = veqt.Id, quantity = 10m, valuePerUnit = 50m, asOfDate = "2026-04-23" }, CT);

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{accountId}/opening-balances", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<List<OpeningBalanceItem>>(CT);
        Assert.Equal(2, items!.Count);
    }

    [Fact]
    public async Task GetSingle_ReturnsOpeningBalance()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Single-acct");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 2000m, asOfDate = "2026-04-23" }, CT);

        var resp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(2000m, body!.Quantity);
    }

    [Fact]
    public async Task GetSingle_NotFound_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Empty-acct");

        var resp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_ChangesQuantity()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Patch-acct");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}",
            new { quantity = 1500m, asOfDate = "2026-04-23" }, CT);

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(1500m, body!.Quantity);
    }

    [Fact]
    public async Task Delete_RemovesOpeningBalance()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Del-acct");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        var delResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var getResp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    private record OpeningBalanceItem(
        Guid TransactionId, Guid AccountId, Guid CommodityId,
        decimal Quantity, decimal Value, decimal? ValuePerUnit, DateOnly AsOfDate);
}
