using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class OpeningBalanceServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ISystemAccountProvider _systemAccounts = null!;
    private IAuditContext _audit = null!;
    private OpeningBalanceService _sut = null!;

    private Commodity _usd = null!;
    private Commodity _cad = null!;
    private Commodity _veqt = null!;
    private Account _openingBalances = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();

        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "US Dollar", Kind = CommodityKind.Currency, DisplayPrecision = 2, IsSystemManaged = true };
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency, DisplayPrecision = 2, IsSystemManaged = true };
        _veqt = new Commodity { Id = Guid.NewGuid(), Symbol = "VEQT", Exchange = "TSX", Name = "Vanguard All-Equity", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.AddRange(_usd, _cad, _veqt);

        _openingBalances = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Equity:Opening Balances",
            AccountType = AccountType.Equity,
            Kind = null,
            ReportingCurrencyId = _usd.Id,
            IsSystem = true,
            IsArchived = false,
        };
        _db.Accounts.Add(_openingBalances);

        await _db.SaveChangesAsync();

        _systemAccounts = Substitute.For<ISystemAccountProvider>();
        _systemAccounts.GetOpeningBalancesAsync(Arg.Any<CancellationToken>())
            .Returns(_openingBalances);

        _audit = Substitute.For<IAuditContext>();

        var linkedPrices = new LinkedPriceManager(_db, _audit);
        _sut = new OpeningBalanceService(_db, _systemAccounts, new TransactionValidator(), linkedPrices, _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> AddAccountAsync(AccountKind kind, Guid? reportingCurrencyId = null, string name = "Test")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = kind.ToAccountType(),
            Kind = kind,
            ReportingCurrencyId = reportingCurrencyId ?? _cad.Id,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset NoonUtc(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static AccountTransactionSplit CashSplit(Guid accountId, Guid commodityId, decimal quantity) =>
        new() { Id = Guid.NewGuid(), AccountId = accountId, CommodityId = commodityId, Quantity = quantity };

    [Fact]
    public async Task Create_CashAccount_ProducesTwoBalancedSplits_AtMidnightUtc()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == cash.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);

        Assert.Equal(_cad.Id, accountSplit.CommodityId);
        Assert.Equal(_cad.Id, equitySplit.CommodityId);
        Assert.Equal(1000m, accountSplit.Quantity);
        Assert.Equal(-1000m, equitySplit.Quantity);
        Assert.Null(accountSplit.UnitPriceRate);
        Assert.Null(equitySplit.UnitPriceRate);
        Assert.Equal(StartOfDayUtc(new DateOnly(2026, 4, 23)), txn.OccurredAt);
    }

    [Fact]
    public async Task Create_CreditCardAccount_AppliesLiabilitySignFlip()
    {
        var cc = await AddAccountAsync(AccountKind.CreditCard, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cc.Id, _cad.Id, 500m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == cc.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);

        Assert.Equal(-500m, accountSplit.Quantity);
        Assert.Equal(500m, equitySplit.Quantity);
        Assert.Equal(_cad.Id, accountSplit.CommodityId);
        Assert.Equal(_cad.Id, equitySplit.CommodityId);
    }

    [Fact]
    public async Task Create_NonInvestmentAccount_RejectsForeignCommodity()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateOpeningBalanceRequest(cash.Id, _usd.Id, 100m, null, new DateOnly(2026, 4, 23)),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithForeignCashCommodity_BalancesInNativeCurrency_AtNoonUtc()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _usd.Id, 5000m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(_usd.Id, accountSplit.CommodityId);
        Assert.Equal(_usd.Id, equitySplit.CommodityId);
        Assert.Equal(5000m, accountSplit.Quantity);
        Assert.Equal(-5000m, equitySplit.Quantity);
        Assert.Null(accountSplit.UnitPriceRate);
        Assert.Null(equitySplit.UnitPriceRate);
        Assert.Equal(NoonUtc(new DateOnly(2026, 4, 23)), txn.OccurredAt);
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithSecurity_EmitsLinkedPriceRow_AtNoonUtc()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, 35.12m, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(_veqt.Id, accountSplit.CommodityId);
        Assert.Equal(_cad.Id, equitySplit.CommodityId);
        Assert.Equal(100m, accountSplit.Quantity);
        Assert.Equal(35.12m, accountSplit.UnitPriceRate);
        Assert.Equal(_cad.Id, accountSplit.UnitPriceCommodityId);
        Assert.Equal(-3512m, equitySplit.Quantity);
        Assert.Null(equitySplit.UnitPriceRate);

        var expectedAt = NoonUtc(new DateOnly(2026, 4, 23));
        Assert.Equal(expectedAt, txn.OccurredAt);

        var price = await _db.CommodityPrices.SingleAsync(p =>
            p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id);
        Assert.Equal(35.12m, price.Rate);
        Assert.Equal(expectedAt, price.PriceAt);
        Assert.Equal(txn.Id, price.CreatedByTransactionId);
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithSecurity_NoPrice_RecordsUnpricedPosition()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var accountSplit = txn.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);

        // Both splits in the security — the transaction balances per-commodity (+100/-100 VEQT).
        Assert.Equal(_veqt.Id, accountSplit.CommodityId);
        Assert.Equal(_veqt.Id, equitySplit.CommodityId);
        Assert.Equal(100m, accountSplit.Quantity);
        Assert.Equal(-100m, equitySplit.Quantity);
        Assert.Null(accountSplit.UnitPriceRate);
        Assert.Null(equitySplit.UnitPriceRate);

        var priceCount = await _db.CommodityPrices.CountAsync(p =>
            p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, priceCount);
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithSecurity_CollidingPrice_Throws()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 4, 23);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _veqt.Id,
            QuoteCurrencyId = _cad.Id,
            PriceAt = NoonUtc(date),
            Rate = 40m,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, 35.12m, date),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SameAccountAndCommodityTwice_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 500m, null, new DateOnly(2026, 4, 24)),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SystemAccount_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateOpeningBalanceRequest(_openingBalances.Id, _usd.Id, 100m, null, new DateOnly(2026, 4, 23)),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ArchivedAccount_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        cash.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 100m, null, new DateOnly(2026, 4, 23)),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangesQuantity_RebalancesSplits()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(1500m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var accountSplit = updated.Splits.Single(s => s.AccountId == cash.Id);
        var equitySplit = updated.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(1500m, accountSplit.Quantity);
        Assert.Equal(-1500m, equitySplit.Quantity);
    }

    [Fact]
    public async Task Update_MovesDateForward_PastLaterTransaction_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var ob = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 1, 1)),
            TestContext.Current.CancellationToken);

        var laterTxn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = StartOfDayUtc(new DateOnly(2026, 2, 15)),
            Memo = "Transfer",
            Type = TransactionType.Normal,
            Splits =
            [
                CashSplit(cash.Id, _cad.Id, 100m),
                CashSplit(_openingBalances.Id, _cad.Id, -100m),
            ],
        };
        _db.AccountTransactions.Add(laterTxn);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(ob.Id,
                new UpdateOpeningBalanceRequest(1000m, null, new DateOnly(2026, 3, 1)),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_MovesDateOnSecurity_MovesLinkedPriceRow_InPlace()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var oldDate = new DateOnly(2026, 1, 1);
        var newDate = new DateOnly(2026, 2, 1);

        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, 30m, oldDate),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(100m, 35m, newDate),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        Assert.Equal(NoonUtc(newDate), updated.OccurredAt);

        var prices = await _db.CommodityPrices
            .Where(p => p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id)
            .ToListAsync();

        var price = Assert.Single(prices);
        Assert.Equal(NoonUtc(newDate), price.PriceAt);
        Assert.Equal(35m, price.Rate);
        Assert.Equal(created.Id, price.CreatedByTransactionId);
    }

    [Fact]
    public async Task Update_MovesDateOnSecurity_WhenTargetInstantHasPrice_Throws()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var oldDate = new DateOnly(2026, 1, 1);
        var newDate = new DateOnly(2026, 2, 1);

        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, 30m, oldDate),
            TestContext.Current.CancellationToken);

        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _veqt.Id,
            QuoteCurrencyId = _cad.Id,
            PriceAt = NoonUtc(newDate),
            Rate = 42m,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.Id,
                new UpdateOpeningBalanceRequest(100m, 35m, newDate),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_UnpricedSecurity_AddPrice_CreatesLinkedRow_AndFlipsEquityCommodity()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 1, 1);

        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, null, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(100m, 30m, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var accountSplit = updated.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = updated.Splits.Single(s => s.AccountId == _openingBalances.Id);

        Assert.Equal(_veqt.Id, accountSplit.CommodityId);
        Assert.Equal(30m, accountSplit.UnitPriceRate);
        Assert.Equal(_cad.Id, accountSplit.UnitPriceCommodityId);
        // Equity leg's commodity flipped from the security to the reporting currency.
        Assert.Equal(_cad.Id, equitySplit.CommodityId);
        Assert.Equal(-3000m, equitySplit.Quantity);
        Assert.Null(equitySplit.UnitPriceRate);

        var price = await _db.CommodityPrices.SingleAsync(p =>
            p.CreatedByTransactionId == created.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(30m, price.Rate);
        Assert.Equal(NoonUtc(date), price.PriceAt);
    }

    [Fact]
    public async Task Update_PricedSecurity_RemovePrice_DeletesLinkedRow_AndFlipsEquityCommodityBack()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 1, 1);

        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, 30m, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(100m, null, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var accountSplit = updated.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = updated.Splits.Single(s => s.AccountId == _openingBalances.Id);

        Assert.Null(accountSplit.UnitPriceRate);
        Assert.Null(accountSplit.UnitPriceCommodityId);
        // Equity leg's commodity flipped back to the security.
        Assert.Equal(_veqt.Id, equitySplit.CommodityId);
        Assert.Equal(-100m, equitySplit.Quantity);

        var priceCount = await _db.CommodityPrices.CountAsync(p =>
            p.CreatedByTransactionId == created.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, priceCount);
    }

    [Fact]
    public async Task Update_UnpricedSecurity_StaysUnpriced_WhenPriceNotProvided()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 1, 1);

        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, null, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(150m, null, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var accountSplit = updated.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = updated.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(150m, accountSplit.Quantity);
        Assert.Null(accountSplit.UnitPriceRate);
        Assert.Equal(_veqt.Id, equitySplit.CommodityId);
        Assert.Equal(-150m, equitySplit.Quantity);

        var priceCount = await _db.CommodityPrices.CountAsync(p =>
            p.CreatedByTransactionId == created.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, priceCount);
    }

    [Fact]
    public async Task Update_ChangesRateOnSecurity_SameDate_UpdatesLinkedRowInPlace()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 1, 1);

        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(invest.Id, _veqt.Id, 100m, 30m, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(100m, 42m, date),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var accountSplit = updated.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = updated.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(42m, accountSplit.UnitPriceRate);
        Assert.Equal(-4200m, equitySplit.Quantity);

        var price = await _db.CommodityPrices.SingleAsync(p =>
            p.CreatedByTransactionId == created.Id);
        Assert.Equal(42m, price.Rate);
        Assert.Equal(NoonUtc(date), price.PriceAt);
    }

    [Fact]
    public async Task Create_BlankMemo_FallsBackToOpeningBalanceDefault()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 100m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Opening balance", txn.Memo);
    }

    [Fact]
    public async Task Create_NonBlankMemo_PersistsTrimmedValue()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);

        var txn = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 100m, null, new DateOnly(2026, 4, 23),
                Memo: "  Carried from old tracker  "),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Carried from old tracker", txn.Memo);
    }

    [Fact]
    public async Task Update_ReplacesMemo()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var date = new DateOnly(2026, 4, 23);
        var created = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 100m, null, date, Memo: "Initial"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id,
            new UpdateOpeningBalanceRequest(100m, null, date, Memo: "Updated"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        Assert.Equal("Updated", updated.Memo);
    }

    [Fact]
    public async Task Delete_WithNoOtherActivity_Succeeds()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var ob = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var touched = await _sut.DeleteAsync(ob.Id, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.AccountTransactions.CountAsync(t => t.Id == ob.Id));
        Assert.Contains(touched, t => t.AccountId == cash.Id && t.CommodityId == _cad.Id);
    }

    [Fact]
    public async Task Delete_WithOtherActivity_RemovesOpeningBalance_AndLeavesOtherTransactionsIntact()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var ob = await _sut.CreateAsync(
            new CreateOpeningBalanceRequest(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 1, 1)),
            TestContext.Current.CancellationToken);

        var laterTxn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = StartOfDayUtc(new DateOnly(2026, 2, 15)),
            Memo = "Transfer",
            Type = TransactionType.Normal,
            Splits =
            [
                CashSplit(cash.Id, _cad.Id, 100m),
                CashSplit(_openingBalances.Id, _cad.Id, -100m),
            ],
        };
        _db.AccountTransactions.Add(laterTxn);
        await _db.SaveChangesAsync();

        var touched = await _sut.DeleteAsync(ob.Id, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.AccountTransactions.CountAsync(t => t.Id == ob.Id));
        Assert.Equal(2, await _db.AccountTransactionSplits.CountAsync(s => s.TransactionId == laterTxn.Id));
        Assert.Contains(touched, t => t.AccountId == cash.Id && t.CommodityId == _cad.Id);
    }
}
