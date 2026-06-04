using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class TradeServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private TradeService _sut = null!;

    private Commodity _cad = null!;
    private Commodity _veqt = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _veqt = new Commodity { Id = Guid.NewGuid(), Symbol = "VEQT", Name = "Vanguard All-Equity", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.AddRange(_cad, _veqt);
        await _db.SaveChangesAsync();

        _audit = Substitute.For<IAuditContext>();
        var validator = new TransactionValidator(_db);
        var linkedPrices = new LinkedPriceManager(_db, _audit);
        _sut = new TradeService(_db, validator, linkedPrices, _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> AddAccount(AccountKind kind, Guid currencyId, string name)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = kind.ToAccountType(),
            Kind = kind,
            ReportingCurrencyId = currencyId,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private CreateTradeRequest BuyRequest(Guid accountId, decimal qty = 2m, decimal price = 50m) =>
        new(accountId, TradeSide.Buy, _veqt.Id, _cad.Id, qty, price,
            new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null);

    [Fact]
    public async Task Create_Buy_BothLegsOnAccount_SecurityLegAnnotated_EmitsLinkedPrice()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");

        var result = await _sut.CreateAsync(BuyRequest(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.Equal(NormalShape.InvestmentBuy, txn.NormalShape);

        // Both legs on the same investment account.
        Assert.All(txn.Splits, s => Assert.Equal(investment.Id, s.AccountId));

        var cash = txn.Splits.Single(s => s.UnitPriceRate is null);
        var sec = txn.Splits.Single(s => s.UnitPriceRate is not null);

        // Buy: cash out (-100 CAD), units in (+2 VEQT).
        Assert.Equal(_cad.Id, cash.CommodityId);
        Assert.Equal(-100m, cash.Quantity);

        Assert.Equal(_veqt.Id, sec.CommodityId);
        Assert.Equal(2m, sec.Quantity);
        Assert.Equal(50m, sec.UnitPriceRate);
        Assert.Equal(_cad.Id, sec.UnitPriceCommodityId);

        // The annotation materialises exactly one linked price row.
        var price = await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(_veqt.Id, price.CommodityId);
        Assert.Equal(_cad.Id, price.QuoteCurrencyId);
        Assert.Equal(50m, price.Rate);
        Assert.Equal(txn.Id, price.CreatedByTransactionId);
    }

    [Fact]
    public async Task Create_Sell_InvertsLegSigns()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");

        var result = await _sut.CreateAsync(
            new CreateTradeRequest(investment.Id, TradeSide.Sell, _veqt.Id, _cad.Id, 2m, 50m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.Equal(NormalShape.InvestmentSell, txn.NormalShape);

        var cash = txn.Splits.Single(s => s.UnitPriceRate is null);
        var sec = txn.Splits.Single(s => s.UnitPriceRate is not null);

        // Sell: cash in (+100 CAD), units out (-2 VEQT).
        Assert.Equal(100m, cash.Quantity);
        Assert.Equal(-2m, sec.Quantity);
        Assert.Equal(50m, sec.UnitPriceRate);
    }

    [Fact]
    public async Task Create_NonInvestmentAccount_Throws()
    {
        var cash = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(BuyRequest(cash.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SecurityIdIsCurrency_Throws()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");
        var usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(usd);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // SecurityId points at a currency.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateTradeRequest(investment.Id, TradeSide.Buy, usd.Id, _cad.Id, 2m, 50m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_FundingIdIsSecurity_Throws()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");
        var other = new Commodity { Id = Guid.NewGuid(), Symbol = "XEQT", Name = "iShares All-Equity", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.Add(other);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateTradeRequest(investment.Id, TradeSide.Buy, _veqt.Id, other.Id, 2m, 50m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NonPositiveQuantity_Throws()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(BuyRequest(investment.Id, qty: 0m), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangesQuantityAndPrice_MovesLinkedPrice()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");

        var created = await _sut.CreateAsync(BuyRequest(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var splitIds = created.Transaction!.Splits.Select(s => s.Id).OrderBy(x => x).ToList();

        var updated = await _sut.UpdateAsync(created.Transaction!.Id,
            new UpdateTradeRequest(investment.Id, TradeSide.Buy, _veqt.Id, _cad.Id, 3m, 60m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), "rebalance"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)updated.Transaction!;
        var cash = txn.Splits.Single(s => s.UnitPriceRate is null);
        var sec = txn.Splits.Single(s => s.UnitPriceRate is not null);

        Assert.Equal(-180m, cash.Quantity);   // 3 * 60
        Assert.Equal(3m, sec.Quantity);
        Assert.Equal(60m, sec.UnitPriceRate);

        // Split Ids preserved across the edit.
        Assert.Equal(splitIds, txn.Splits.Select(s => s.Id).OrderBy(x => x).ToList());

        var price = await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(60m, price.Rate);
    }

    [Fact]
    public async Task Update_OnDripHalf_Throws()
    {
        // A trade that is half of a DRIP (RelatedTransactionId set) must not be editable via the
        // standalone trade editor — that would desync the pair's balance.
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "Drip-RRSP");

        var created = await _sut.CreateAsync(BuyRequest(investment.Id), TestContext.Current.CancellationToken);
        created.Transaction!.RelatedTransactionId = Guid.NewGuid();
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.Transaction!.Id,
                new UpdateTradeRequest(investment.Id, TradeSide.Buy, _veqt.Id, _cad.Id, 3m, 60m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_SideChange_Throws()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");

        var created = await _sut.CreateAsync(BuyRequest(investment.Id), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.Transaction!.Id,
                new UpdateTradeRequest(investment.Id, TradeSide.Sell, _veqt.Id, _cad.Id, 2m, 50m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }
}
