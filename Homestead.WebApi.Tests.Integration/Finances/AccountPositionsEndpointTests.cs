using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class AccountPositionsEndpointTests : IntegrationTestBase
{
    public AccountPositionsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task SetDisplayCurrencyAsync(HttpClient client, Guid commodityId)
    {
        var resp = await SendJsonPatchAsync(client, "/api/v1/finances/settings", new[]
        {
            new { op = "replace", path = "/displayCurrencyId", value = (object)commodityId.ToString() }
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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
            name = symbol,
            displayPrecision = 4,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        return body!.Id;
    }

    private async Task AddOpeningPositionAsync(HttpClient client, Guid accountId, Guid commodityId,
        decimal quantity, decimal? valuePerUnit = null, string asOfDate = "2026-01-01")
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId, quantity, valuePerUnit, asOfDate }, CT);
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetPositions_NonInvestmentAccount_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            kind = "Cash",
            reportingCurrencyId = cad,
            initialBalance = new { amount = 1000m, asOfDate = "2026-01-01" },
        }, CT);
        var cash = await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{cash!.Id}/positions", CT);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetPositions_MissingAccount_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{Guid.NewGuid()}/positions", CT);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetPositions_EmptyInvestmentAccount_ReturnsEmptyList()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var inv = await CreateInvestmentAccountAsync(client, cad, "RRSP");

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{inv}/positions", CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<FinancePositionItem>>(CT);
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task GetPositions_PricedPositions_SortedDescByDisplayValueAndShareSumsToOne()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var inv = await CreateInvestmentAccountAsync(client, cad, "RRSP");
        // Two priced positions both in CAD: 1000 and 250.
        await AddOpeningPositionAsync(client, inv, cad, 1000m);

        // Add a second priced position via a security with a unit price.
        var aapl = await CreateSecurityAsync(client, "AAPL", "NASDAQ");
        await AddOpeningPositionAsync(client, inv, aapl, 10m, valuePerUnit: 25m);

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{inv}/positions", CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<FinancePositionItem>>(CT);

        Assert.NotNull(body);
        Assert.Equal(2, body!.Count);
        // Sorted desc: CAD 1000 > AAPL @ 25 * 10 = 250.
        Assert.Equal(cad, body[0].CommodityId);
        Assert.Equal(1000m, body[0].DisplayValue);
        Assert.Equal(aapl, body[1].CommodityId);
        Assert.Equal(250m, body[1].DisplayValue);

        // Shares sum to 1.0.
        var totalShare = body.Sum(p => p.ShareOfAccount ?? 0m);
        Assert.Equal(1m, totalShare);
        Assert.Equal(1000m / 1250m, body[0].ShareOfAccount);
        Assert.Equal(250m / 1250m, body[1].ShareOfAccount);
    }

    [Fact]
    public async Task GetPositions_UnpricedHoldings_TrailAndHaveNullShare()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var inv = await CreateInvestmentAccountAsync(client, cad, "RRSP");

        // Priced cash.
        await AddOpeningPositionAsync(client, inv, cad, 1000m);
        // Unpriced security ZZZA (later in alphabet).
        var zzza = await CreateSecurityAsync(client, "ZZZA", "OTC");
        await AddOpeningPositionAsync(client, inv, zzza, 5m);
        // Unpriced security AAAA (earlier in alphabet) — should sort before ZZZA among unpriced.
        var aaaa = await CreateSecurityAsync(client, "AAAA", "OTC");
        await AddOpeningPositionAsync(client, inv, aaaa, 5m);

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{inv}/positions", CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<List<FinancePositionItem>>(CT);

        Assert.NotNull(body);
        Assert.Equal(3, body!.Count);
        // Priced first.
        Assert.Equal(cad, body[0].CommodityId);
        Assert.NotNull(body[0].DisplayValue);
        Assert.Equal(1m, body[0].ShareOfAccount);
        // Unpriced trailing in symbol asc order: AAAA, then ZZZA.
        Assert.Equal(aaaa, body[1].CommodityId);
        Assert.Null(body[1].ShareOfAccount);
        Assert.Null(body[1].DisplayValue);
        Assert.Equal(zzza, body[2].CommodityId);
        Assert.Null(body[2].ShareOfAccount);
    }
}
