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
            name = "Vanguard All-Equity",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        Assert.Equal("VEQT", body!.Symbol);
        Assert.Equal("Security", body.Kind);
    }

    [Fact]
    public async Task CreateCommodity_DuplicateSymbol_Returns409()
    {
        var client = await CreateAdminClientAsync();

        var first = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "AAPL",
            name = "Apple Inc.",
            displayPrecision = 4
        }, CT);
        first.EnsureSuccessStatusCode();

        var dup = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "AAPL",
            name = "Apple Duplicate",
            displayPrecision = 4
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task CreateCommodity_DuplicateOfSeededCurrency_Returns409()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "USD",
            name = "Shadow USD",
            displayPrecision = 2
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PatchCommodity_UpdatesName_AndAuditsChange()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "MSFT",
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
    public async Task DeleteCommodity_UnusedSecurity_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "TSLA",
            name = "Tesla",
            displayPrecision = 4
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodities/{created!.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
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
}
