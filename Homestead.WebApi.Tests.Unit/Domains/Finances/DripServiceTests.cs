using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class DripServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ISystemAccountProvider _systemAccounts = null!;
    private IAuditContext _audit = null!;
    private DripService _sut = null!;

    private Commodity _usd = null!;
    private Commodity _vfv = null!;
    private Account _income = null!;

    private static readonly DateTimeOffset At = new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _vfv = new Commodity { Id = Guid.NewGuid(), Symbol = "VFV", Name = "Vanguard S&P 500", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.AddRange(_usd, _vfv);

        _income = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Income",
            AccountType = AccountType.Income,
            Kind = null,
            ReportingCurrencyId = _usd.Id,
            IsSystem = true,
            IsArchived = false,
        };
        _db.Accounts.Add(_income);
        await _db.SaveChangesAsync();

        _systemAccounts = Substitute.For<ISystemAccountProvider>();
        _systemAccounts.GetIncomeAsync(Arg.Any<CancellationToken>()).Returns(_income);

        _audit = Substitute.For<IAuditContext>();
        _sut = new DripService(
            _db, _systemAccounts, new TransactionValidator(_db), new LinkedPriceManager(_db, _audit), _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> AddInvestmentAsync(string name = "RRSP")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = AccountKind.Investment.ToAccountType(),
            Kind = AccountKind.Investment,
            ReportingCurrencyId = _usd.Id,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private CreateDripRequest Request(Guid accountId, decimal amount = 5m, decimal units = 0.05m,
        string? description = null) =>
        new(accountId, _vfv.Id, _usd.Id, amount, units, At, description);

    [Fact]
    public async Task Create_BuildsTwoLinkedHalves_CorrectSignsPairingAndPrice()
    {
        var investment = await AddInvestmentAsync();

        var result = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dividend = (NormalTransaction)result.DividendHalf!;
        var buy = (NormalTransaction)result.BuyHalf!;

        Assert.Equal(NormalShape.Dividend, dividend.NormalShape);
        Assert.Equal(NormalShape.InvestmentBuy, buy.NormalShape);

        // Paired both ways.
        Assert.Equal(buy.Id, dividend.RelatedTransactionId);
        Assert.Equal(dividend.Id, buy.RelatedTransactionId);

        // Dividend half: +5 into the account, -5 to Income.
        var divAccountLeg = dividend.Splits.Single(s => s.AccountId == investment.Id);
        var divIncomeLeg = dividend.Splits.Single(s => s.AccountId == _income.Id);
        Assert.Equal(5m, divAccountLeg.Quantity);
        Assert.Equal(_usd.Id, divAccountLeg.CommodityId);
        Assert.Equal(-5m, divIncomeLeg.Quantity);

        // Buy half: both legs on the investment account; cash out -5, units +0.05 @ 100.
        Assert.All(buy.Splits, s => Assert.Equal(investment.Id, s.AccountId));
        var cash = buy.Splits.Single(s => s.UnitPriceRate is null);
        var sec = buy.Splits.Single(s => s.UnitPriceRate is not null);
        Assert.Equal(_usd.Id, cash.CommodityId);
        Assert.Equal(-5m, cash.Quantity);
        Assert.Equal(_vfv.Id, sec.CommodityId);
        Assert.Equal(0.05m, sec.Quantity);
        Assert.Equal(100m, sec.UnitPriceRate);   // derived: 5 / 0.05
        Assert.Equal(_usd.Id, sec.UnitPriceCommodityId);

        // Only the Buy half is annotated -> exactly one linked price row.
        var price = await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(_vfv.Id, price.CommodityId);
        Assert.Equal(_usd.Id, price.QuoteCurrencyId);
        Assert.Equal(100m, price.Rate);
        Assert.Equal(buy.Id, price.CreatedByTransactionId);
    }

    [Fact]
    public async Task Create_NonTerminatingPrice_BothHalvesBalance_AndCashNetsToZero()
    {
        // 5 / 0.0523 is non-terminating in decimal; the literal "-Amount" cash leg would fail the
        // weight-balance validator. The service derives total = Units * unitPrice so both halves
        // balance exactly and cash nets to zero.
        var investment = await AddInvestmentAsync();

        var amount = 5m;
        var units = 0.0523m;
        var unitPrice = amount / units;
        var total = units * unitPrice;
        Assert.NotEqual(total, amount);   // guard: this case actually exercises the rounding edge

        var result = await _sut.CreateAsync(
            Request(investment.Id, amount, units), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dividend = (NormalTransaction)result.DividendHalf!;
        var buy = (NormalTransaction)result.BuyHalf!;

        var divAccountLeg = dividend.Splits.Single(s => s.AccountId == investment.Id);
        var cash = buy.Splits.Single(s => s.UnitPriceRate is null);
        var sec = buy.Splits.Single(s => s.UnitPriceRate is not null);

        Assert.Equal(total, divAccountLeg.Quantity);
        Assert.Equal(-total, cash.Quantity);
        Assert.Equal(units, sec.Quantity);
        Assert.Equal(unitPrice, sec.UnitPriceRate);

        // Cash on the account nets to exactly zero: dividend inflow + buy outflow.
        Assert.Equal(0m, divAccountLeg.Quantity + cash.Quantity);
        // Buy half balances per commodity in the payment bucket: cash + units * price == 0.
        Assert.Equal(0m, cash.Quantity + sec.Quantity * sec.UnitPriceRate!.Value);
    }

    [Fact]
    public async Task Create_BlankDescription_UsesDefaultMemos()
    {
        var investment = await AddInvestmentAsync();

        var result = await _sut.CreateAsync(
            Request(investment.Id, description: "   "), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Dividend", result.DividendHalf!.Memo);
        Assert.Equal("Reinvest VFV dividend", result.BuyHalf!.Memo);
    }

    [Fact]
    public async Task Create_Description_AppliesToBothHalves()
    {
        var investment = await AddInvestmentAsync();

        var result = await _sut.CreateAsync(
            Request(investment.Id, description: "  Q1 distribution  "), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Q1 distribution", result.DividendHalf!.Memo);
        Assert.Equal("Q1 distribution", result.BuyHalf!.Memo);
    }

    [Fact]
    public async Task Create_NonInvestmentAccount_Throws()
    {
        var cash = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Chequing",
            AccountType = AccountKind.Cash.ToAccountType(),
            Kind = AccountKind.Cash,
            ReportingCurrencyId = _usd.Id,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(cash);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Request(cash.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NonPositiveAmount_Throws()
    {
        var investment = await AddInvestmentAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Request(investment.Id, amount: 0m), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NonPositiveUnits_Throws()
    {
        var investment = await AddInvestmentAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Request(investment.Id, units: 0m), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SecurityIdIsCurrency_Throws()
    {
        var investment = await AddInvestmentAsync();

        // SecurityId points at a currency (USD), payment is a security (VFV).
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateDripRequest(investment.Id, _usd.Id, _vfv.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_PaymentIdIsSecurity_Throws()
    {
        var investment = await AddInvestmentAsync();
        var other = new Commodity { Id = Guid.NewGuid(), Symbol = "XEQT", Name = "iShares", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.Add(other);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateDripRequest(investment.Id, _vfv.Id, other.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SecurityEqualsPayment_Throws()
    {
        var investment = await AddInvestmentAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateDripRequest(investment.Id, _usd.Id, _usd.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_RewritesBothHalves_PreservingSplitIds()
    {
        var investment = await AddInvestmentAsync();

        var created = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var dividendId = created.DividendHalf!.Id;
        var allSplitIds = created.DividendHalf!.Splits.Concat(created.BuyHalf!.Splits)
            .Select(s => s.Id).OrderBy(x => x).ToList();

        // Click the Dividend half; edit amount + units + date.
        var updated = await _sut.UpdateAsync(dividendId,
            new UpdateDripRequest(investment.Id, _vfv.Id, _usd.Id, 12m, 0.1m,
                new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), "Revised"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dividend = (NormalTransaction)updated.DividendHalf!;
        var buy = (NormalTransaction)updated.BuyHalf!;

        Assert.Equal("Revised", dividend.Memo);
        Assert.Equal("Revised", buy.Memo);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), dividend.OccurredAt);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), buy.OccurredAt);

        var divAccountLeg = dividend.Splits.Single(s => s.AccountId == investment.Id);
        var cash = buy.Splits.Single(s => s.UnitPriceRate is null);
        var sec = buy.Splits.Single(s => s.UnitPriceRate is not null);
        Assert.Equal(12m, divAccountLeg.Quantity);
        Assert.Equal(-12m, cash.Quantity);
        Assert.Equal(0.1m, sec.Quantity);
        Assert.Equal(120m, sec.UnitPriceRate);   // 12 / 0.1

        // Split Ids preserved across the edit.
        var afterSplitIds = dividend.Splits.Concat(buy.Splits).Select(s => s.Id).OrderBy(x => x).ToList();
        Assert.Equal(allSplitIds, afterSplitIds);

        // The linked price row moved/updated to the new rate + date.
        var price = await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(120m, price.Rate);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), price.PriceAt);
    }

    [Fact]
    public async Task Update_ResavingSeededAutoMemo_KeepsAsymmetricDefaults_NoDriftOntoDividend()
    {
        // A blank-description DRIP stores asymmetric default memos ("Dividend" on the dividend half,
        // "Reinvest VFV dividend" on the buy half). The editor seeds its single description field from
        // the Buy half's memo, so an edit that leaves the description untouched sends that auto-string
        // back. The service must treat it as blank and regenerate the defaults, NOT promote
        // "Reinvest VFV dividend" onto the dividend half.
        var investment = await AddInvestmentAsync();

        var created = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Dividend", created.DividendHalf!.Memo);
        Assert.Equal("Reinvest VFV dividend", created.BuyHalf!.Memo);

        // Re-save with the seeded Buy-half memo as the description, changing only the date.
        var updated = await _sut.UpdateAsync(created.DividendHalf!.Id,
            new UpdateDripRequest(investment.Id, _vfv.Id, _usd.Id, 5m, 0.05m,
                new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), "Reinvest VFV dividend"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // No drift: the dividend half keeps its "Dividend" default; the buy half is unchanged.
        Assert.Equal("Dividend", updated.DividendHalf!.Memo);
        Assert.Equal("Reinvest VFV dividend", updated.BuyHalf!.Memo);
    }

    [Fact]
    public async Task Update_ChangingAccount_Throws()
    {
        var investment = await AddInvestmentAsync();
        var other = await AddInvestmentAsync("TFSA");

        var created = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.DividendHalf!.Id,
                new UpdateDripRequest(other.Id, _vfv.Id, _usd.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangingSecurity_Throws()
    {
        var investment = await AddInvestmentAsync();
        var other = new Commodity { Id = Guid.NewGuid(), Symbol = "XEQT", Name = "iShares", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.Add(other);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var created = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.DividendHalf!.Id,
                new UpdateDripRequest(investment.Id, other.Id, _usd.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangingPaymentCurrency_Throws()
    {
        var investment = await AddInvestmentAsync();
        var cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(cad);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var created = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.DividendHalf!.Id,
                new UpdateDripRequest(investment.Id, _vfv.Id, cad.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_NonDripTransaction_Throws()
    {
        // A standalone dividend (no RelatedTransactionId) is not a DRIP pair.
        var investment = await AddInvestmentAsync();
        var standalone = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = At,
            Memo = "Dividend",
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Dividend,
            Splits =
            [
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = investment.Id, CommodityId = _usd.Id, Quantity = 5m },
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = _income.Id, CommodityId = _usd.Id, Quantity = -5m },
            ],
        };
        _db.AccountTransactions.Add(standalone);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(standalone.Id,
                new UpdateDripRequest(investment.Id, _vfv.Id, _usd.Id, 5m, 0.05m, At, null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteBoth_RemovesBothHalvesAndPriceRow()
    {
        var investment = await AddInvestmentAsync();

        var created = await _sut.CreateAsync(Request(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var dividendId = created.DividendHalf!.Id;
        var buyId = created.BuyHalf!.Id;

        // Click the Buy half; delete both.
        await _sut.DeleteBothAsync(buyId, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _db.AccountTransactions
            .CountAsync(t => t.Id == dividendId || t.Id == buyId, TestContext.Current.CancellationToken));
        Assert.Equal(0, await _db.CommodityPrices
            .CountAsync(p => p.CreatedByTransactionId == buyId, TestContext.Current.CancellationToken));
    }
}
