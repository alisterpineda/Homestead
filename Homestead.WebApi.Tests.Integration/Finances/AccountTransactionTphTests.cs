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

    [Fact]
    public async Task BalanceEntry_RoundTripsThroughTph()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var account = await CreateAccountAsync(db, usd);

        var balanceEntry = new BalanceEntryTransaction
        {
            TxnDate = new DateOnly(2026, 1, 31),
            Description = "January statement",
            CurrencyId = usd.Id,
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 1234.56m,
            Note = "From statement PDF",
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
        Assert.Equal("From statement PDF", loaded.Note);

        // Discriminator column persisted as the enum name, not its integer value.
        // Alias as "Value" because EF's SqlQuery<string> maps the unique column to that name.
        var storedType = await db.Database
            .SqlQuery<string>($"SELECT \"Type\" AS \"Value\" FROM finances.account_transactions WHERE \"Id\" = {balanceEntry.Id}")
            .SingleAsync(CT);
        Assert.Equal("BalanceEntry", storedType);
    }

    [Fact]
    public async Task FilteredUniqueIndex_RejectsDuplicateBalanceEntries_SameAccountCommodityDate()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usd = await db.Commodities.SingleAsync(c => c.Symbol == "USD", CT);
        var account = await CreateAccountAsync(db, usd);
        var date = new DateOnly(2026, 1, 31);

        db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            TxnDate = date,
            Description = "First assertion",
            CurrencyId = usd.Id,
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 100m,
        });
        await db.SaveChangesAsync(CT);

        db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            TxnDate = date,
            Description = "Second, conflicting",
            CurrencyId = usd.Id,
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
        var date = new DateOnly(2026, 1, 31);

        db.AccountTransactions.AddRange(
            new BalanceEntryTransaction
            {
                TxnDate = date,
                Description = "Chequing balance",
                CurrencyId = usd.Id,
                AccountId = accountA.Id,
                AssertedCommodityId = usd.Id,
                AssertedQuantity = 500m,
            },
            new BalanceEntryTransaction
            {
                TxnDate = date,
                Description = "Savings balance",
                CurrencyId = usd.Id,
                AccountId = accountB.Id,
                AssertedCommodityId = usd.Id,
                AssertedQuantity = 10_000m,
            });

        await db.SaveChangesAsync(CT);

        var count = await db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .CountAsync(t => t.TxnDate == date, CT);
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

        // A balance entry establishes a filtered-index row on (account, usd, date).
        db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            TxnDate = date,
            Description = "Month-end balance",
            CurrencyId = usd.Id,
            AccountId = account.Id,
            AssertedCommodityId = usd.Id,
            AssertedQuantity = 100m,
        });
        await db.SaveChangesAsync(CT);

        // A NormalTransaction on the same date must not collide — it has no
        // AccountId/AssertedCommodityId, so the filter excludes it.
        db.AccountTransactions.Add(new NormalTransaction
        {
            TxnDate = date,
            Description = "Itemised transfer",
            CurrencyId = usd.Id,
        });

        await db.SaveChangesAsync(CT); // should not throw
    }

    private async Task<Account> CreateAccountAsync(AppDbContext db, Commodity defaultCommodity, string name = "Test account")
    {
        var account = new Account
        {
            Name = name,
            AccountType = AccountType.Asset,
            DefaultCommodityId = defaultCommodity.Id,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(CT);
        return account;
    }
}
