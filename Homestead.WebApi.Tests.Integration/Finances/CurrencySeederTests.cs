using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class CurrencySeederTests : IntegrationTestBase
{
    public CurrencySeederTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Seeder_PopulatedCurrenciesOnFirstBoot()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currencyCount = await db.Commodities
            .CountAsync(c => c.Kind == CommodityKind.Currency, CT);

        Assert.Equal(CurrencySeeder.Iso4217Currencies.Count, currencyCount);
    }

    [Fact]
    public async Task Seeder_WellKnownCurrenciesHaveCorrectPrecision()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var jpy = await db.Commodities.SingleAsync(c => c.Symbol == "JPY", CT);
        var kwd = await db.Commodities.SingleAsync(c => c.Symbol == "KWD", CT);

        Assert.Equal(CommodityKind.Currency, usd.Kind);
        Assert.Equal(2, usd.DisplayPrecision);
        Assert.Equal(0, jpy.DisplayPrecision);
        Assert.Equal(3, kwd.DisplayPrecision);
    }

    [Fact]
    public async Task Seeder_SeededCurrenciesAreSystemManaged_AndHaveNullExchange()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currencies = await db.Commodities
            .Where(c => c.Kind == CommodityKind.Currency)
            .ToListAsync(CT);

        Assert.All(currencies, c => Assert.True(c.IsSystemManaged));
        Assert.All(currencies, c => Assert.Null(c.Exchange));
    }

    [Fact]
    public async Task Seeder_IsIdempotent_OnRepeatedRuns()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var before = await db.Commodities.CountAsync(CT);

        await CurrencySeeder.SeedAsync(db, false, CT);
        await CurrencySeeder.SeedAsync(db, false, CT);

        var after = await db.Commodities.CountAsync(CT);
        Assert.Equal(before, after);
    }
}
