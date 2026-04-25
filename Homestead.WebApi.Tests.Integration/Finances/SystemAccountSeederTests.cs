using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class SystemAccountSeederTests : IntegrationTestBase
{
    public SystemAccountSeederTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Seeder_CreatesAllRequiredSystemAccounts_WithCorrectFlags()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var openingBalances = await db.Accounts.AsNoTracking()
            .SingleAsync(a => a.Name == SystemAccountSeeder.OpeningBalancesName, CT);
        var adjustments = await db.Accounts.AsNoTracking()
            .SingleAsync(a => a.Name == SystemAccountSeeder.AdjustmentsName, CT);
        var income = await db.Accounts.AsNoTracking()
            .SingleAsync(a => a.Name == SystemAccountSeeder.IncomeName, CT);
        var expense = await db.Accounts.AsNoTracking()
            .SingleAsync(a => a.Name == SystemAccountSeeder.ExpenseName, CT);

        Assert.Equal(AccountType.Equity, openingBalances.AccountType);
        Assert.Equal(AccountType.Equity, adjustments.AccountType);
        Assert.Equal(AccountType.Income, income.AccountType);
        Assert.Equal(AccountType.Expense, expense.AccountType);

        foreach (var account in new[] { openingBalances, adjustments, income, expense })
        {
            Assert.True(account.IsSystem);
            Assert.False(account.IsArchived);
            Assert.Null(account.Kind);
        }
    }

    [Fact]
    public async Task Seeder_IsIdempotent_OnRepeatedRuns()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var initialCount = await db.Accounts.CountAsync(a => a.IsSystem, CT);

        SystemAccountSeeder.Seed(db);
        await SystemAccountSeeder.SeedAsync(db, cancellationToken: CT);

        var finalCount = await db.Accounts.CountAsync(a => a.IsSystem, CT);
        Assert.Equal(initialCount, finalCount);
    }

    [Fact]
    public async Task Seeder_RestoresAccounts_AfterRespawnWipe()
    {
        // The test base runs the seeder after every Respawn reset — if that
        // glue broke, the first account-dependent assertion would fail. This
        // test pins the expectation explicitly.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var systemCount = await db.Accounts.CountAsync(a => a.IsSystem, CT);
        Assert.Equal(4, systemCount);
    }
}
