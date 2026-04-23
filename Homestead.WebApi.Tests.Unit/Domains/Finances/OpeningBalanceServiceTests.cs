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

        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "US Dollar", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _veqt = new Commodity { Id = Guid.NewGuid(), Symbol = "VEQT", Name = "Vanguard All-Equity", Kind = CommodityKind.Security, DisplayPrecision = 4 };
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

        _sut = new OpeningBalanceService(_db, _systemAccounts, new TransactionValidator(), _audit);
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

    [Fact]
    public async Task Create_CashAccount_ProducesTwoBalancedSplits()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);

        var txn = await _sut.CreateAsync(cash.Id, _cad.Id, quantity: 1000m,
            valuePerUnit: null, asOfDate: new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == cash.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);

        Assert.Equal(1000m, accountSplit.Quantity);
        Assert.Equal(1000m, accountSplit.Value);
        Assert.Equal(-1000m, equitySplit.Quantity);
        Assert.Equal(-1000m, equitySplit.Value);
        Assert.Equal(_cad.Id, txn.CurrencyId);
    }

    [Fact]
    public async Task Create_CreditCardAccount_AppliesLiabilitySignFlip()
    {
        var cc = await AddAccountAsync(AccountKind.CreditCard, _cad.Id);

        var txn = await _sut.CreateAsync(cc.Id, _cad.Id, quantity: 500m,
            valuePerUnit: null, asOfDate: new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == cc.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);

        Assert.Equal(-500m, accountSplit.Value);
        Assert.Equal(500m, equitySplit.Value);
    }

    [Fact]
    public async Task Create_NonInvestmentAccount_RejectsForeignCommodity()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(cash.Id, _usd.Id, 100m, null, new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithForeignCashCommodity_BalancesInNativeCurrency()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);

        var txn = await _sut.CreateAsync(invest.Id, _usd.Id, quantity: 5000m,
            valuePerUnit: null, asOfDate: new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        Assert.Equal(_usd.Id, txn.CurrencyId);
        var accountSplit = txn.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(_usd.Id, accountSplit.CommodityId);
        Assert.Equal(_usd.Id, equitySplit.CommodityId);
        Assert.Equal(5000m, accountSplit.Value);
        Assert.Equal(-5000m, equitySplit.Value);
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithSecurity_EmitsPriceRow()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);

        var txn = await _sut.CreateAsync(invest.Id, _veqt.Id, quantity: 100m,
            valuePerUnit: 35.12m, asOfDate: new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync();

        Assert.Equal(_cad.Id, txn.CurrencyId);
        var accountSplit = txn.Splits.Single(s => s.AccountId == invest.Id);
        var equitySplit = txn.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(_veqt.Id, accountSplit.CommodityId);
        Assert.Equal(_cad.Id, equitySplit.CommodityId);
        Assert.Equal(100m, accountSplit.Quantity);
        Assert.Equal(3512m, accountSplit.Value);
        Assert.Equal(-3512m, equitySplit.Quantity);
        Assert.Equal(-3512m, equitySplit.Value);

        var price = await _db.CommodityPrices.SingleAsync(p =>
            p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id);
        Assert.Equal(35.12m, price.Rate);
        Assert.Equal(new DateOnly(2026, 4, 23), price.PriceDate);
    }

    [Fact]
    public async Task Create_InvestmentAccount_WithSecurity_ReusesExistingPrice()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 4, 23);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _veqt.Id,
            QuoteCurrencyId = _cad.Id,
            PriceDate = date,
            Rate = 40m,
        });
        await _db.SaveChangesAsync();

        var txn = await _sut.CreateAsync(invest.Id, _veqt.Id, quantity: 100m,
            valuePerUnit: 35.12m /* ignored because a price exists */, asOfDate: date, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var accountSplit = txn.Splits.Single(s => s.AccountId == invest.Id);
        Assert.Equal(4000m, accountSplit.Value); // 100 × 40 (existing rate)

        var priceCount = await _db.CommodityPrices.CountAsync(p =>
            p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id && p.PriceDate == date);
        Assert.Equal(1, priceCount);
    }

    [Fact]
    public async Task Create_SameAccountAndCommodityTwice_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await _sut.CreateAsync(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(cash.Id, _cad.Id, 500m, null, new DateOnly(2026, 4, 24), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SystemAccount_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(_openingBalances.Id, _usd.Id, 100m, null, new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ArchivedAccount_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        cash.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(cash.Id, _cad.Id, 100m, null, new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangesQuantity_RebalancesSplits()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var created = await _sut.CreateAsync(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id, quantity: 1500m, valuePerUnit: null,
            asOfDate: new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var accountSplit = updated.Splits.Single(s => s.AccountId == cash.Id);
        var equitySplit = updated.Splits.Single(s => s.AccountId == _openingBalances.Id);
        Assert.Equal(1500m, accountSplit.Value);
        Assert.Equal(-1500m, equitySplit.Value);
    }

    [Fact]
    public async Task Update_MovesDateForward_PastLaterTransaction_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var ob = await _sut.CreateAsync(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 1, 1), TestContext.Current.CancellationToken);

        // Simulate a later transaction touching the same (account, commodity).
        var laterTxn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            TxnDate = new DateOnly(2026, 2, 15),
            Description = "Transfer",
            CurrencyId = _cad.Id,
            Type = TransactionType.Normal,
            Splits =
            [
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = cash.Id, CommodityId = _cad.Id, Quantity = 100m, Value = 100m },
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = _openingBalances.Id, CommodityId = _cad.Id, Quantity = -100m, Value = -100m },
            ],
        };
        _db.AccountTransactions.Add(laterTxn);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(ob.Id, 1000m, null, new DateOnly(2026, 3, 1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_MovesDateOnSecurity_LeavesOldPriceRow()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var oldDate = new DateOnly(2026, 1, 1);
        var newDate = new DateOnly(2026, 2, 1);

        var created = await _sut.CreateAsync(invest.Id, _veqt.Id, 100m, valuePerUnit: 30m,
            asOfDate: oldDate, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id, quantity: 100m, valuePerUnit: 35m,
            asOfDate: newDate, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        Assert.Equal(newDate, updated.TxnDate);

        var prices = await _db.CommodityPrices
            .Where(p => p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id)
            .OrderBy(p => p.PriceDate)
            .ToListAsync();

        Assert.Equal(2, prices.Count);
        Assert.Equal(oldDate, prices[0].PriceDate);
        Assert.Equal(30m, prices[0].Rate);
        Assert.Equal(newDate, prices[1].PriceDate);
        Assert.Equal(35m, prices[1].Rate);
    }

    [Fact]
    public async Task Update_MovesDateOnSecurity_WhenTargetDateHasPrice_Throws()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var oldDate = new DateOnly(2026, 1, 1);
        var newDate = new DateOnly(2026, 2, 1);

        var created = await _sut.CreateAsync(invest.Id, _veqt.Id, 100m, valuePerUnit: 30m,
            asOfDate: oldDate, TestContext.Current.CancellationToken);

        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _veqt.Id,
            QuoteCurrencyId = _cad.Id,
            PriceDate = newDate,
            Rate = 42m,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.Id, 100m, 35m, newDate, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangesRateOnSecurity_SameDate_UpdatesInPlace()
    {
        var invest = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        var date = new DateOnly(2026, 1, 1);

        var created = await _sut.CreateAsync(invest.Id, _veqt.Id, 100m, valuePerUnit: 30m,
            asOfDate: date, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdateAsync(created.Id, 100m, valuePerUnit: 42m,
            asOfDate: date, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        var accountSplit = updated.Splits.Single(s => s.AccountId == invest.Id);
        Assert.Equal(4200m, accountSplit.Value);

        var price = await _db.CommodityPrices.SingleAsync(p =>
            p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id && p.PriceDate == date);
        Assert.Equal(42m, price.Rate);
    }

    [Fact]
    public async Task Delete_WithNoOtherActivity_Succeeds()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var ob = await _sut.CreateAsync(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 4, 23), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        await _sut.DeleteAsync(ob.Id, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.AccountTransactions.CountAsync(t => t.Id == ob.Id));
    }

    [Fact]
    public async Task Delete_WithOtherActivity_Throws()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var ob = await _sut.CreateAsync(cash.Id, _cad.Id, 1000m, null, new DateOnly(2026, 1, 1), TestContext.Current.CancellationToken);

        var laterTxn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            TxnDate = new DateOnly(2026, 2, 15),
            Description = "Transfer",
            CurrencyId = _cad.Id,
            Type = TransactionType.Normal,
            Splits =
            [
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = cash.Id, CommodityId = _cad.Id, Quantity = 100m, Value = 100m },
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = _openingBalances.Id, CommodityId = _cad.Id, Quantity = -100m, Value = -100m },
            ],
        };
        _db.AccountTransactions.Add(laterTxn);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.DeleteAsync(ob.Id, TestContext.Current.CancellationToken));
    }
}
