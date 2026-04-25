using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class BalanceEntryServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ISystemAccountProvider _systemAccounts = null!;
    private IAuditContext _audit = null!;
    private BalanceEntryService _sut = null!;
    private BalanceEntryReconciler _reconciler = null!;

    private Commodity _cad = null!;
    private Account _adjustments = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(_cad);

        _adjustments = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Equity:Adjustments",
            AccountType = AccountType.Equity,
            Kind = null,
            ReportingCurrencyId = _cad.Id,
            IsSystem = true,
            IsArchived = false,
        };
        _db.Accounts.Add(_adjustments);
        await _db.SaveChangesAsync();

        _systemAccounts = Substitute.For<ISystemAccountProvider>();
        _systemAccounts.GetAdjustmentsAsync(Arg.Any<CancellationToken>()).Returns(_adjustments);

        _audit = Substitute.For<IAuditContext>();
        _sut = new BalanceEntryService(_db, _systemAccounts, new TransactionValidator(), _audit);
        _reconciler = new BalanceEntryReconciler(_db, _systemAccounts, _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> AddAccountAsync(AccountKind kind, string name = "Cash")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = kind.ToAccountType(),
            Kind = kind,
            ReportingCurrencyId = _cad.Id,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Create_FirstAssertion_Prior0_RecordsDeltaEqualToAsserted()
    {
        var cash = await AddAccountAsync(AccountKind.Cash);

        var result = await _sut.CreateAsync(
            new CreateBalanceEntryRequest(cash.Id, _cad.Id, 1500m, new DateOnly(2026, 4, 30), null),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = result.Entry!;
        Assert.Equal(1500m, entry.AssertedQuantity);
        var accountSplit = entry.Splits.Single(s => s.AccountId == cash.Id);
        var adjSplit = entry.Splits.Single(s => s.AccountId == _adjustments.Id);
        Assert.Equal(1500m, accountSplit.Quantity);
        Assert.Equal(-1500m, adjSplit.Quantity);
        Assert.Equal(new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero), entry.OccurredAt);
    }

    [Fact]
    public async Task Create_AfterPriorActivity_RecordsDeltaEqualToAssertedMinusPrior()
    {
        var cash = await AddAccountAsync(AccountKind.Cash);

        // Seed a prior transfer: +$100 on 2026-01-01.
        var priorTxn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Description = "Transfer",
            Type = TransactionType.Normal,
            Splits =
            [
                new() { Id = Guid.NewGuid(), AccountId = cash.Id, CommodityId = _cad.Id, Quantity = 100m },
                new() { Id = Guid.NewGuid(), AccountId = _adjustments.Id, CommodityId = _cad.Id, Quantity = -100m },
            ],
        };
        _db.AccountTransactions.Add(priorTxn);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.CreateAsync(
            new CreateBalanceEntryRequest(cash.Id, _cad.Id, 250m, new DateOnly(2026, 4, 30), null),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = result.Entry!;
        var accountSplit = entry.Splits.Single(s => s.AccountId == cash.Id);
        var adjSplit = entry.Splits.Single(s => s.AccountId == _adjustments.Id);

        Assert.Equal(150m, accountSplit.Quantity);
        Assert.Equal(-150m, adjSplit.Quantity);
    }

    [Fact]
    public async Task Create_LiabilityAccount_AppliesSignFlipOnAsserted()
    {
        var creditCard = await AddAccountAsync(AccountKind.CreditCard, "CC");

        var result = await _sut.CreateAsync(
            new CreateBalanceEntryRequest(creditCard.Id, _cad.Id, 500m, new DateOnly(2026, 4, 30), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = result.Entry!;
        Assert.Equal(-500m, entry.AssertedQuantity);
        var accountSplit = entry.Splits.Single(s => s.AccountId == creditCard.Id);
        var adjSplit = entry.Splits.Single(s => s.AccountId == _adjustments.Id);
        Assert.Equal(-500m, accountSplit.Quantity);
        Assert.Equal(500m, adjSplit.Quantity);
    }

    [Fact]
    public async Task Create_DuplicateSameDate_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash);

        await _sut.CreateAsync(
            new CreateBalanceEntryRequest(cash.Id, _cad.Id, 100m, new DateOnly(2026, 4, 30), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateBalanceEntryRequest(cash.Id, _cad.Id, 200m, new DateOnly(2026, 4, 30), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NonInvestmentForeignCommodity_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash);
        var usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(usd);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateBalanceEntryRequest(cash.Id, usd.Id, 100m, new DateOnly(2026, 4, 30), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reconciler_AfterMidChainTransferEdit_UpdatesDownstreamBalanceEntries()
    {
        var cash = await AddAccountAsync(AccountKind.Cash);

        // BE1 on Jan 31 asserting $100 (prior = 0).
        var be1 = await _sut.CreateAsync(
            new CreateBalanceEntryRequest(cash.Id, _cad.Id, 100m, new DateOnly(2026, 1, 31), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // BE2 on Feb 28 asserting $250 (prior = 100, delta = 150).
        var be2 = await _sut.CreateAsync(
            new CreateBalanceEntryRequest(cash.Id, _cad.Id, 250m, new DateOnly(2026, 2, 28), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Insert a transfer +$50 on Jan 15 (between BE1 and BE2). This should shift BE1's prior
        // to $50 (delta = $50) and leave BE2's prior at $50+$50 = $100 (delta = $150 unchanged).
        var transfer = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            Description = "Transfer",
            Type = TransactionType.Normal,
            Splits =
            [
                new() { Id = Guid.NewGuid(), AccountId = cash.Id, CommodityId = _cad.Id, Quantity = 50m },
                new() { Id = Guid.NewGuid(), AccountId = _adjustments.Id, CommodityId = _cad.Id, Quantity = -50m },
            ],
        };
        _db.AccountTransactions.Add(transfer);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _reconciler.ReconcileAsync(
            new[] { new TouchedPair(cash.Id, _cad.Id, transfer.OccurredAt) },
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Reload from DB.
        var be1Reloaded = await _db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .Include(e => e.Splits)
            .SingleAsync(e => e.Id == be1.Entry!.Id, TestContext.Current.CancellationToken);
        var be1Account = be1Reloaded.Splits.Single(s => s.AccountId == cash.Id);
        var be1Adj = be1Reloaded.Splits.Single(s => s.AccountId == _adjustments.Id);

        Assert.Equal(50m, be1Account.Quantity);
        Assert.Equal(-50m, be1Adj.Quantity);

        var be2Reloaded = await _db.AccountTransactions
            .OfType<BalanceEntryTransaction>()
            .Include(e => e.Splits)
            .SingleAsync(e => e.Id == be2.Entry!.Id, TestContext.Current.CancellationToken);
        var be2Account = be2Reloaded.Splits.Single(s => s.AccountId == cash.Id);

        // BE2 prior is now $100 (50 + 50), asserted = 250, delta = 150. Same as before.
        Assert.Equal(150m, be2Account.Quantity);
    }
}
