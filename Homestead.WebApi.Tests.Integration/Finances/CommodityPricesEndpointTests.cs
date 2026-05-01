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

    private async Task<Guid> SeedPriceAsync(
        Guid commodityId, Guid quoteCurrencyId, DateTimeOffset priceAt, decimal rate,
        CommodityPriceSource source, Guid? createdByTransactionId = null)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = commodityId,
            QuoteCurrencyId = quoteCurrencyId,
            PriceAt = priceAt,
            Rate = rate,
            Source = source,
            CreatedByTransactionId = createdByTransactionId,
        };
        db.CommodityPrices.Add(price);
        await db.SaveChangesAsync(CT);
        return price.Id;
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
        Assert.Equal("Manual", body.Source);
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
    public async Task CreatePrice_ManualCoexistsWithTransactionLinkedAtSameInstant()
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
                Memo = "Seeded buy",
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
                Source = CommodityPriceSource.Transaction,
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

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var verifyScope = CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await verifyDb.CommodityPrices
            .Where(p => p.CommodityId == usdId && p.QuoteCurrencyId == cadId && p.PriceAt == PriceAtJan31)
            .ToListAsync(CT);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Id == linkedPriceId && r.Source == CommodityPriceSource.Transaction);
        Assert.Contains(rows, r => r.Source == CommodityPriceSource.Manual && r.Rate == 1.5000m);
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
    public async Task PatchPrice_UpdatesPriceAt_Succeeds()
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

        var newPriceAt = new DateTimeOffset(2026, 1, 31, 14, 30, 0, TimeSpan.Zero);
        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{created!.Id}",
            new[] { new { op = "replace", path = "/priceAt", value = (object)newPriceAt } });

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<FinancePriceItem>(CT);
        Assert.Equal(newPriceAt, patched!.PriceAt);
        Assert.Equal(1.3456m, patched.Rate);
    }

    [Fact]
    public async Task PatchPrice_PriceAtCollidesWithExisting_Returns409()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var firstResp = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = PriceAtJan15,
            rate = 1.30m
        }, CT);
        firstResp.EnsureSuccessStatusCode();

        var secondResp = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = PriceAtJan31,
            rate = 1.40m
        }, CT);
        var second = await secondResp.Content.ReadFromJsonAsync<FinancePriceItem>(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{second!.Id}",
            new[] { new { op = "replace", path = "/priceAt", value = (object)PriceAtJan15 } });

        Assert.Equal(HttpStatusCode.Conflict, patchResp.StatusCode);
    }

    [Fact]
    public async Task PatchPrice_PriceAtMovesOverTransactionLinkedInstant_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transaction = new NormalTransaction
            {
                Id = Guid.NewGuid(),
                OccurredAt = PriceAtJan15,
                Memo = "Seeded buy",
                Type = TransactionType.Normal,
            };
            db.AccountTransactions.Add(transaction);
            db.CommodityPrices.Add(new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = usdId,
                QuoteCurrencyId = cadId,
                PriceAt = PriceAtJan15,
                Rate = 1.30m,
                Source = CommodityPriceSource.Transaction,
                CreatedByTransactionId = transaction.Id,
            });
            await db.SaveChangesAsync(CT);
        }

        var manualResp = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId = usdId,
            quoteCurrencyId = cadId,
            priceAt = PriceAtJan31,
            rate = 1.40m
        }, CT);
        var manual = await manualResp.Content.ReadFromJsonAsync<FinancePriceItem>(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{manual!.Id}",
            new[] { new { op = "replace", path = "/priceAt", value = (object)PriceAtJan15 } });

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<FinancePriceItem>(CT);
        Assert.Equal(PriceAtJan15, patched!.PriceAt);
    }

    [Fact]
    public async Task PatchPrice_PriceAtTooFarInFuture_Returns400()
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

        var farFuture = DateTimeOffset.UtcNow.AddDays(5);
        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{created!.Id}",
            new[] { new { op = "replace", path = "/priceAt", value = (object)farFuture } });

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
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
                Memo = "Seeded buy",
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
                Source = CommodityPriceSource.Transaction,
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
                Memo = "Seeded buy",
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
                Source = CommodityPriceSource.Transaction,
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
        Assert.All(body.Items, item => Assert.Equal("Manual", item.Source));
    }

    [Fact]
    public async Task GetPrices_BankOfCanadaSource_ReturnsSourceField()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        await SeedPriceAsync(usdId, cadId, PriceAtJan15, 1.3456m, CommodityPriceSource.BankOfCanada);

        var response = await client.GetAsync(
            $"/api/v1/finances/commodity-prices?page=1&pageSize=10&commodityId={usdId}&quoteCurrencyId={cadId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinancePriceItem>>(CT);
        var row = Assert.Single(body!.Items);
        Assert.Equal("BankOfCanada", row.Source);
        Assert.Null(row.CreatedByTransactionId);
    }

    [Fact]
    public async Task PatchPrice_BankOfCanadaSource_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var bocPriceId = await SeedPriceAsync(usdId, cadId, PriceAtJan31, 1.3456m, CommodityPriceSource.BankOfCanada);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/commodity-prices/{bocPriceId}",
            new[] { new { op = "replace", path = "/rate", value = (object)1.4000m } });

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task DeletePrice_BankOfCanadaSource_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var bocPriceId = await SeedPriceAsync(usdId, cadId, PriceAtJan31, 1.3456m, CommodityPriceSource.BankOfCanada);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodity-prices/{bocPriceId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var verifyScope = CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.CommodityPrices.AnyAsync(p => p.Id == bocPriceId, CT));
    }

    [Fact]
    public async Task DeletePrice_AlphaVantageSource_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var (usdId, cadId, _) = await SetupCommoditiesAsync(client);

        var avPriceId = await SeedPriceAsync(usdId, cadId, PriceAtJan31, 1.3456m, CommodityPriceSource.AlphaVantage);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodity-prices/{avPriceId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var verifyScope = CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.CommodityPrices.AnyAsync(p => p.Id == avPriceId, CT));
    }

    [Fact]
    public async Task DeletePrice_ManualSource_Returns204()
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

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/commodity-prices/{created!.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var verifyScope = CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.CommodityPrices.AnyAsync(p => p.Id == created.Id, CT));
    }
}
