using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class CommodityPricesEndpointTests : IntegrationTestBase
{
    public CommodityPricesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<(Guid usdId, Guid cadId, Guid veqtId)> SetupCommoditiesAsync(HttpClient client)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usdId = await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
        var cadId = await db.Commodities.Where(c => c.Symbol == "CAD").Select(c => c.Id).SingleAsync(CT);

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "VEQT",
            name = "Vanguard All-Equity",
            displayPrecision = 4
        }, CT);
        createResp.EnsureSuccessStatusCode();
        var veqt = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        return (usdId, cadId, veqt!.Id);
    }

    [Fact]
    public async Task CreatePrice_ValidUsdToCad_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceDate = "2026-01-31",
            rate = 1.3456m
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrice_NonCurrencyQuote_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (_, cadId, veqtId) = await SetupCommoditiesAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = cadId,
            quoteCurrencyId = veqtId,
            priceDate = "2026-01-31",
            rate = 1.0m
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrice_ZeroOrNegativeRate_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var zero = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceDate = "2026-01-31",
            rate = 0m
        }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var negative = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceDate = "2026-01-31",
            rate = -0.5m
        }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task CreatePrice_DuplicateKey_Returns409()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var first = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceDate = "2026-01-31",
            rate = 1.3456m
        }, CT);
        first.EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceDate = "2026-01-31",
            rate = 1.4000m
        }, CT);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task PatchPrice_UpdatesRate_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceDate = "2026-01-31",
            rate = 1.3456m
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinancePriceItem>(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{created!.Id}",
            new[] { new { op = "replace", path = "/rate", value = (object)1.4000m } });

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<FinancePriceItem>(CT);
        Assert.Equal(1.4000m, patched!.Rate);
    }

    [Fact]
    public async Task GetPrices_FiltersAndOrdersDescending()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        foreach (var (date, rate) in new[] { ("2026-01-01", 1.30m), ("2026-01-15", 1.33m), ("2026-01-31", 1.35m) })
        {
            await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
            {
                commodityId = usdId,
                quoteCurrencyId = cadId,
                priceDate = date,
                rate
            }, CT);
        }

        var response = await client.GetAsync(
            $"/api/v1/finances/commodity-prices?page=1&pageSize=10&commodityId={usdId}&quoteCurrencyId={cadId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinancePriceItem>>(CT);
        Assert.Equal(3, body!.TotalCount);
        Assert.Equal(new DateOnly(2026, 1, 31), body.Items[0].PriceDate);
        Assert.Equal(new DateOnly(2026, 1, 1), body.Items[2].PriceDate);
    }
}
