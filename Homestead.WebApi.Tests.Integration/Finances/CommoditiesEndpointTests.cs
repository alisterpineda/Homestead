using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class CommoditiesEndpointTests : IntegrationTestBase
{
    public CommoditiesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetCommodities_FiltersByKindAndSearch()
    {
        var client = await CreateAdminClientAsync();

        var currenciesResp = await client.GetAsync("/api/v1/finances/commodities?page=1&pageSize=5&kind=Currency", CT);
        Assert.Equal(HttpStatusCode.OK, currenciesResp.StatusCode);
        var currencies = await currenciesResp.Content.ReadFromJsonAsync<PaginatedResult<FinanceCommodityItem>>(CT);
        Assert.All(currencies!.Items, c => Assert.Equal("Currency", c.Kind));
        Assert.All(currencies.Items, c => Assert.True(c.IsSystemManaged));
        Assert.All(currencies.Items, c => Assert.Null(c.Exchange));

        var usdResp = await client.GetAsync("/api/v1/finances/commodities?page=1&pageSize=50&search=USD", CT);
        Assert.Equal(HttpStatusCode.OK, usdResp.StatusCode);
        var usd = await usdResp.Content.ReadFromJsonAsync<PaginatedResult<FinanceCommodityItem>>(CT);
        Assert.Contains(usd!.Items, c => c.Symbol == "USD");
    }

    [Fact]
    public async Task CreateCommodity_Security_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "VEQT",
            exchange = "TSX",
            name = "Vanguard All-Equity",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal("VEQT", body!.Symbol);
        Assert.Equal("TSX", body.Exchange);
        Assert.Equal("Security", body.Kind);
        Assert.False(body.IsSystemManaged);
    }

    [Fact]
    public async Task CreateCommodity_EmptyExchange_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "VFV",
            exchange = "",
            name = "Vanguard S&P 500 ETF",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCommodity_UppercasesSymbolAndExchange()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "googl",
            exchange = "nasdaq",
            name = "Alphabet",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal("GOOGL", body!.Symbol);
        Assert.Equal("NASDAQ", body.Exchange);
    }

    [Fact]
    public async Task CreateCommodity_WhitespaceOnlySymbol_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "   ",
            exchange = "NASDAQ",
            name = "Whitespace symbol",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCommodity_WhitespaceOnlyExchange_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "WSEX",
            exchange = "   ",
            name = "Whitespace exchange",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCommodity_TrimsLeadingTrailingWhitespace()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "  vfv  ",
            exchange = "  tsx  ",
            name = "  Vanguard S&P 500 ETF  ",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal("VFV", body!.Symbol);
        Assert.Equal("TSX", body.Exchange);
        Assert.Equal("Vanguard S&P 500 ETF", body.Name);
    }

    [Fact]
    public async Task CreateCommodity_DuplicateSymbolAndExchange_Returns409()
    {
        var client = await CreateAdminClientAsync();

        var first = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "AAPL",
            exchange = "NASDAQ",
            name = "Apple Inc.",
            displayPrecision = 4
        }, CT);
        first.EnsureSuccessStatusCode();

        var dup = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "AAPL",
            exchange = "NASDAQ",
            name = "Apple Duplicate",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task CreateCommodity_SameSymbolDifferentExchange_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        var first = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "BRK",
            exchange = "NYSE",
            name = "Berkshire Hathaway",
            displayPrecision = 4
        }, CT);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "BRK",
            exchange = "OTHER",
            name = "Berkshire Hathaway Other",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task PatchCommodity_UpdatesName_AndAuditsChange()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "MSFT",
            exchange = "NASDAQ",
            name = "Microsoft",
            displayPrecision = 4
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodities/{created!.Id}",
            new[]
            {
                new { op = "replace", path = "/name", value = (object)"Microsoft Corporation" }
            });

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal("Microsoft Corporation", patched!.Name);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditCount = await db.AuditLogs.CountAsync(
            a => a.EntityType == "Commodity" && a.EntityId == created.Id, CT);
        Assert.Equal(2, auditCount); // Created + Updated
    }

    [Fact]
    public async Task PatchCommodity_SeededCurrency_Returns400()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cadId = await db.Commodities.Where(c => c.Symbol == "CAD").Select(c => c.Id).SingleAsync(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodities/{cadId}",
            new[]
            {
                new { op = "replace", path = "/name", value = (object)"Custom CAD" }
            });

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task DeleteCommodity_UnusedSecurity_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "TSLA",
            exchange = "NASDAQ",
            name = "Tesla",
            displayPrecision = 4
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodities/{created!.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteCommodity_SeededCurrency_Returns400()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Pick a currency that isn't referenced by any account (CAD is seeded but may be referenced).
        // Use a less common code to keep this test isolated from the rest of the suite.
        var xofId = await db.Commodities.Where(c => c.Symbol == "XOF").Select(c => c.Id).SingleAsync(CT);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodities/{xofId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteCommodity_ReferencedByAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usdId = await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodities/{usdId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task GetCommodity_NewSecurity_ExposesZeroPricesAndCanDelete()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "NVDA",
            exchange = "NASDAQ",
            name = "Nvidia",
            displayPrecision = 4
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var getResp = await client.GetAsync($"/api/v1/finances/commodities/{created!.Id}", CT);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal(0, fetched!.PriceCount);
        Assert.Null(fetched.LatestPriceAt);
        Assert.True(fetched.CanDelete);
    }

    [Fact]
    public async Task GetCommodity_SecurityWithPrices_ExposesCountAndLatestAndCanNotDelete()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cadId = await db.Commodities.Where(c => c.Symbol == "CAD").Select(c => c.Id).SingleAsync(CT);

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "AMD",
            exchange = "NASDAQ",
            name = "AMD",
            displayPrecision = 4
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var older = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero);

        var p1 = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = created!.Id, quoteCurrencyId = cadId, priceAt = older, rate = 110.5m
        }, CT);
        p1.EnsureSuccessStatusCode();
        var p2 = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = created.Id, quoteCurrencyId = cadId, priceAt = newer, rate = 142.25m
        }, CT);
        p2.EnsureSuccessStatusCode();

        var getResp = await client.GetAsync($"/api/v1/finances/commodities/{created.Id}", CT);
        var fetched = await getResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal(2, fetched!.PriceCount);
        Assert.Equal(newer, fetched.LatestPriceAt);
        Assert.False(fetched.CanDelete);
    }

    [Fact]
    public async Task GetCommodity_SeededCurrency_HasCanDeleteFalse()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cadId = await db.Commodities.Where(c => c.Symbol == "CAD").Select(c => c.Id).SingleAsync(CT);

        var getResp = await client.GetAsync($"/api/v1/finances/commodities/{cadId}", CT);
        var fetched = await getResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.True(fetched!.IsSystemManaged);
        Assert.False(fetched.CanDelete);
    }

    [Fact]
    public async Task GetCommodities_ListIncludesNewFieldsForSecurity()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "META",
            exchange = "NASDAQ",
            name = "Meta",
            displayPrecision = 4
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var listResp = await client.GetAsync(
            $"/api/v1/finances/commodities?page=1&pageSize=200&kind=Security&search=META", CT);
        var list = await listResp.Content.ReadFromJsonAsync<PaginatedResult<FinanceCommodityItem>>(CT);
        var row = Assert.Single(list!.Items, c => c.Id == created!.Id);
        Assert.Equal(0, row.PriceCount);
        Assert.Null(row.LatestPriceAt);
        Assert.True(row.CanDelete);
    }
}
