using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class CommodityPricesEndpointTests : IntegrationTestBase
{
    public CommodityPricesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateTimeOffset PriceAtJan31 = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PriceAtJan15 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PriceAtJan01 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task<(Guid usdId, Guid cadId, Guid veqtId)> SetupCommoditiesAsync(HttpClient client)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usdId = await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
        var cadId = await db.Commodities.Where(c => c.Symbol == "CAD").Select(c => c.Id).SingleAsync(CT);

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "VEQT",
            exchange = "TSX",
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
            priceAt = PriceAtJan31,
            rate = 1.3456m
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinancePriceItem>(CT);
        Assert.Equal(PriceAtJan31, body!.PriceAt);
        Assert.Null(body.CreatedByTransactionId);
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
            priceAt = PriceAtJan31,
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
            priceAt = PriceAtJan31,
            rate = 0m
        }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var negative = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = PriceAtJan31,
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
            priceAt = PriceAtJan31,
            rate = 1.3456m
        }, CT);
        first.EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = PriceAtJan31,
            rate = 1.4000m
        }, CT);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CreatePrice_CollidesWithTransactionLinked_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        // Seed a transaction-linked price directly in the DB to mimic a shape we
        // don't yet have an endpoint for.
        Guid linkedPriceId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transaction = new NormalTransaction
            {
                Id = Guid.NewGuid(),
                OccurredAt = PriceAtJan31,
                Description = "Seeded buy",
                CurrencyId = cadId,
                Type = TransactionType.Normal,
            };
            db.AccountTransactions.Add(transaction);

            var price = new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = usdId,
                QuoteCurrencyId = cadId,
                PriceAt = PriceAtJan31,
                Rate = 1.3456m,
                CreatedByTransactionId = transaction.Id,
            };
            db.CommodityPrices.Add(price);
            await db.SaveChangesAsync(CT);
            linkedPriceId = price.Id;
        }

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = PriceAtJan31,
            rate = 1.5000m
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(Guid.Empty, linkedPriceId);
    }

    [Fact]
    public async Task CreatePrice_FutureBeyondOneDay_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = DateTimeOffset.UtcNow.AddDays(7),
            rate = 1.3456m
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
            priceAt = PriceAtJan31,
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
    public async Task PatchPrice_TransactionLinked_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        Guid linkedPriceId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transaction = new NormalTransaction
            {
                Id = Guid.NewGuid(),
                OccurredAt = PriceAtJan31,
                Description = "Seeded buy",
                CurrencyId = cadId,
                Type = TransactionType.Normal,
            };
            db.AccountTransactions.Add(transaction);

            var price = new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = usdId,
                QuoteCurrencyId = cadId,
                PriceAt = PriceAtJan31,
                Rate = 1.3456m,
                CreatedByTransactionId = transaction.Id,
            };
            db.CommodityPrices.Add(price);
            await db.SaveChangesAsync(CT);
            linkedPriceId = price.Id;
        }

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{linkedPriceId}",
            new[] { new { op = "replace", path = "/rate", value = (object)1.4000m } });

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task DeletePrice_TransactionLinked_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        Guid linkedPriceId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transaction = new NormalTransaction
            {
                Id = Guid.NewGuid(),
                OccurredAt = PriceAtJan31,
                Description = "Seeded buy",
                CurrencyId = cadId,
                Type = TransactionType.Normal,
            };
            db.AccountTransactions.Add(transaction);

            var price = new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = usdId,
                QuoteCurrencyId = cadId,
                PriceAt = PriceAtJan31,
                Rate = 1.3456m,
                CreatedByTransactionId = transaction.Id,
            };
            db.CommodityPrices.Add(price);
            await db.SaveChangesAsync(CT);
            linkedPriceId = price.Id;
        }

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodity-prices/{linkedPriceId}", CT);
        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task GetPrices_FiltersAndOrdersDescending()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        foreach (var (priceAt, rate) in new[]
        {
            (PriceAtJan01, 1.30m),
            (PriceAtJan15, 1.33m),
            (PriceAtJan31, 1.35m)
        })
        {
            await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
            {
                commodityId = usdId,
                quoteCurrencyId = cadId,
                priceAt,
                rate
            }, CT);
        }

        var response = await client.GetAsync(
            $"/api/v1/finances/commodity-prices?page=1&pageSize=10&commodityId={usdId}&quoteCurrencyId={cadId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinancePriceItem>>(CT);
        Assert.Equal(3, body!.TotalCount);
        Assert.Equal(PriceAtJan31, body.Items[0].PriceAt);
        Assert.Equal(PriceAtJan01, body.Items[2].PriceAt);
    }
}
