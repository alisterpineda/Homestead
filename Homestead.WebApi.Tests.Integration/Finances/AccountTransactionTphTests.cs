using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class AccountTransactionTphTests : IntegrationTestBase
{
    public AccountTransactionTphTests(PostgresFixture fixture) : base(fixture) { }

    private static DateTimeOffset BalanceEntryAt(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);

    private static DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    [Fact]
    public async Task BalanceEntry_RoundTripsThroughTph()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var account = await CreateAccountAsync(db, usd);

        var occurredAt = BalanceEntryAt(new DateOnly(2026, 1, 31));
        var balanceEntry = new BalanceEntryTransaction
        {
            OccurredAt = occurredAt,
            Description = "From statement PDF",
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 1234.56m,
        };
        db.AccountTransactions.Add(balanceEntry);
        await db.SaveChangesAsync(CT);

        // Round-trip via OfType<> on the base DbSet — the discriminator should route.
        var loaded = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .SingleAsync(t => t.Id == balanceEntry.Id, CT);

        Assert.Equal(TransactionType.BalanceEntry, loaded.Type);
        Assert.Equal(account.Id, loaded.AccountId);
        Assert.Equal(usd.Id, loaded.AssertedCommodityId);
        Assert.Equal(1234.56m, loaded.AssertedQuantity);
        Assert.Equal("From statement PDF", loaded.Description);
        Assert.Equal(occurredAt, loaded.OccurredAt);

        // Discriminator column persisted as the enum name, not its integer value.
        // Alias as "Value" because EF's SqlQuery<string> maps the unique column to that name.
        var storedType = await db.Database
            .SqlQuery<string>($"SELECT \"Type\" AS \"Value\" FROM finances.account_transactions WHERE \"Id\" = {balanceEntry.Id}")
            .SingleAsync(CT);
        Assert.Equal("BalanceEntry", storedType);
    }

    [Fact]
    public async Task OpeningBalance_RoundTripsThroughTph()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var account = await CreateAccountAsync(db, usd);

        var openingBalance = new OpeningBalanceTransaction
        {
            OccurredAt = StartOfDayUtc(new DateOnly(2026, 1, 1)),
            Description = "Imported from old tracker",
        };
        db.AccountTransactions.Add(openingBalance);
        await db.SaveChangesAsync(CT);

        // Round-trip via OfType<> on the base DbSet.
        var loaded = await db.AccountTransactions
            .OfType<OpeningBalanceTransaction>()
            .SingleAsync(t => t.Id == openingBalance.Id, CT);

        Assert.Equal(TransactionType.OpeningBalance, loaded.Type);
        Assert.Equal("Imported from old tracker", loaded.Description);

        // OpeningBalance is a sibling of NormalTransaction (not a subclass), so it must NOT
        // be returned when the base DbSet is filtered to NormalTransaction.
        var normalCount = await db.AccountTransactions
            .OfType<NormalTransaction>()
            .CountAsync(t => t.Id == openingBalance.Id, CT);
        Assert.Equal(0, normalCount);

        // Discriminator column persisted as the enum name.
        var storedType = await db.Database
            .SqlQuery<string>($"SELECT \"Type\" AS \"Value\" FROM finances.account_transactions WHERE \"Id\" = {openingBalance.Id}")
            .SingleAsync(CT);
        Assert.Equal("OpeningBalance", storedType);
    }

    [Fact]
    public async Task FilteredUniqueIndex_RejectsDuplicateBalanceEntries_SameAccountCommodityDate()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var account = await CreateAccountAsync(db, usd);
        var occurredAt = BalanceEntryAt(new DateOnly(2026, 1, 31));

        db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            OccurredAt = occurredAt,
            Description = "First assertion",
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 100m,
        });
        await db.SaveChangesAsync(CT);

        db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            OccurredAt = occurredAt,
            Description = "Second, conflicting",
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 200m,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(CT));
    }

    [Fact]
    public async Task FilteredUniqueIndex_AllowsBalanceEntries_OnDifferentAccounts_SameDate()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var accountA = await CreateAccountAsync(db, usd, name: "Chequing");
        var accountB = await CreateAccountAsync(db, usd, name: "Savings");
        var occurredAt = BalanceEntryAt(new DateOnly(2026, 1, 31));

        db.AccountTransactions.AddRange(
            new BalanceEntryTransaction
            {
                OccurredAt = occurredAt,
                Description = "Chequing balance",
                AccountId = accountA.Id,
                AssertedCommodityId = usd.Id,
                AssertedQuantity = 500m,
            },
            new BalanceEntryTransaction
            {
                OccurredAt = occurredAt,
                Description = "Savings balance",
                AccountId = accountB.Id,
                AssertedCommodityId = usd.Id,
                AssertedQuantity = 10_000m,
            });

        await db.SaveChangesAsync(CT);

        var count = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .CountAsync(t => t.OccurredAt == occurredAt, CT);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task FilteredUniqueIndex_DoesNotApplyToNormalTransactions()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var account = await CreateAccountAsync(db, usd);
        var date = new DateOnly(2026, 1, 31);

        // A balance entry establishes a filtered-index row on (account, usd, EOD-of-date).
        db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            OccurredAt = BalanceEntryAt(date),
            Description = "Month-end balance",
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 100m,
        });
        await db.SaveChangesAsync(CT);

        // A NormalTransaction on the same date must not collide — it has no
        // AccountId/AssertedCommodityId, so the filter excludes it. OccurredAt at
        // 00:00 UTC on the same date is also a different instant.
        db.AccountTransactions.Add(new NormalTransaction
        {
            OccurredAt = StartOfDayUtc(date),
            Description = "Itemised transfer",
        });

        await db.SaveChangesAsync(CT); // should not throw
    }

    private async Task<Account> CreateAccountAsync(AppDbContext db, Commodity reportingCurrency, string name = "Test account")
    {
        var account = new Account
        {
            Name = name,
            AccountType = AccountType.Asset,
            Kind = AccountKind.Cash,
            ReportingCurrencyId = reportingCurrency.Id,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(CT);
        return account;
    }
}
