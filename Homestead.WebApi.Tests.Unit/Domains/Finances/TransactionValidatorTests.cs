using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class TransactionValidatorTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private TransactionValidator _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _sut = new TransactionValidator(_db);
        await ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task Validate(AccountTransaction txn) =>
        _sut.ValidateAsync(txn, TestContext.Current.CancellationToken);

    private async Task<Commodity> AddCommodity(CommodityKind kind, string symbol)
    {
        var commodity = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = symbol,
            Name = symbol,
            Kind = kind,
            DisplayPrecision = (short)(kind == CommodityKind.Currency ? 2 : 4),
        };
        _db.Commodities.Add(commodity);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return commodity;
    }

    private static AccountTransactionSplit Split(
        Guid commodityId,
        decimal quantity,
        decimal? unitPriceRate = null,
        Guid? unitPriceCommodityId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            CommodityId = commodityId,
            Quantity = quantity,
            UnitPriceRate = unitPriceRate,
            UnitPriceCommodityId = unitPriceCommodityId,
        };

    [Fact]
    public async Task Normal_SameCommodityBalancedSplits_PassesValidation()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Memo = "Transfer",
            Splits = [Split(cad, 100m), Split(cad, -100m)],
        };

        await Validate(txn);
    }

    [Fact]
    public async Task Normal_UnbalancedSplits_ThrowsBusinessRule()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Memo = "Bad transfer",
            Splits = [Split(cad, 100m), Split(cad, -50m)],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("per commodity", ex.Message);
    }

    [Fact]
    public async Task Normal_SingleSplit_ThrowsBusinessRule()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Memo = "Incomplete",
            Splits = [Split(cad, 100m)],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("at least two splits", ex.Message);
    }

    [Fact]
    public async Task Normal_CrossCurrencyPricedTransfer_BalancesViaAnnotation()
    {
        // USD chequing → CAD chequing: -100 USD @ 1.35 CAD ↔ +135 CAD
        var usd = Guid.NewGuid();
        var cad = Guid.NewGuid();

        var txn = new NormalTransaction
        {
            Memo = "Cross-currency transfer",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 1.35m, unitPriceCommodityId: cad),
                Split(cad, 135m),
            ],
        };

        await Validate(txn);
    }

    [Fact]
    public async Task Normal_CrossCurrencyPricedTransfer_Unbalanced_Throws()
    {
        var usd = Guid.NewGuid();
        var cad = Guid.NewGuid();

        var txn = new NormalTransaction
        {
            Memo = "Bad cross-currency transfer",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 1.35m, unitPriceCommodityId: cad),
                Split(cad, 140m),
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("per commodity", ex.Message);
    }

    [Fact]
    public async Task Normal_InvestmentBuy_SecurityLegAnnotatedInFundingCurrency_Balances()
    {
        var cad = Guid.NewGuid();
        var veqt = Guid.NewGuid();

        var txn = new NormalTransaction
        {
            Memo = "Buy 2 VEQT @ 50",
            Splits =
            [
                Split(cad, -100m),
                Split(veqt, 2m, unitPriceRate: 50m, unitPriceCommodityId: cad),
            ],
        };

        await Validate(txn);
    }

    [Fact]
    public async Task Normal_Annotation_MissingCommodity_Throws()
    {
        var cad = Guid.NewGuid();
        var usd = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Memo = "Invalid",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 1.35m),
                Split(cad, 135m),
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("both rate and commodity", ex.Message);
    }

    [Fact]
    public async Task Normal_Annotation_SelfQuote_Throws()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Memo = "Invalid",
            Splits =
            [
                Split(cad, -100m, unitPriceRate: 1m, unitPriceCommodityId: cad),
                Split(cad, 100m),
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public async Task Normal_Annotation_ZeroRate_Throws()
    {
        var usd = Guid.NewGuid();
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Memo = "Invalid",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 0m, unitPriceCommodityId: cad),
                Split(cad, 0m),
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public async Task Normal_Annotation_NonCurrencyCommodity_Throws()
    {
        // A security cannot serve as a quote commodity on a price annotation.
        var cad = await AddCommodity(CommodityKind.Currency, "CAD");
        var veqt = await AddCommodity(CommodityKind.Security, "VEQT");

        var txn = new NormalTransaction
        {
            Memo = "Invalid — security as quote",
            Splits =
            [
                // Annotated leg's weight lands in the veqt bucket (-100 × 2 = -200), balanced by +200 veqt,
                // so the transaction is structurally valid and reaches the currency-kind check.
                Split(cad.Id, -100m, unitPriceRate: 2m, unitPriceCommodityId: veqt.Id),
                Split(veqt.Id, 200m),
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("must be a currency", ex.Message);
    }

    [Fact]
    public async Task Normal_Annotation_CurrencyCommodity_Passes()
    {
        // A currency quote commodity that exists in the DB passes the kind check.
        var usd = await AddCommodity(CommodityKind.Currency, "USD");
        var cad = await AddCommodity(CommodityKind.Currency, "CAD");

        var txn = new NormalTransaction
        {
            Memo = "Cross-currency transfer",
            Splits =
            [
                Split(usd.Id, -100m, unitPriceRate: 1.35m, unitPriceCommodityId: cad.Id),
                Split(cad.Id, 135m),
            ],
        };

        await Validate(txn);
    }

    [Fact]
    public async Task BalanceEntry_TwoSameCommodityUnpricedSplitsOnAccountAndCounterpart_Passes()
    {
        var cad = Guid.NewGuid();
        var account = Guid.NewGuid();
        var adjustments = Guid.NewGuid();

        var txn = new BalanceEntryTransaction
        {
            Memo = "Balance assertion",
            AccountId = account,
            AssertedCommodityId = cad,
            AssertedQuantity = 1500m,
            Splits =
            [
                new()
                {
                    Id = Guid.NewGuid(),
                    AccountId = account,
                    CommodityId = cad,
                    Quantity = 50m,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    AccountId = adjustments,
                    CommodityId = cad,
                    Quantity = -50m,
                },
            ],
        };

        await Validate(txn);
    }

    [Fact]
    public async Task BalanceEntry_WrongSplitCount_Throws()
    {
        var cad = Guid.NewGuid();
        var txn = new BalanceEntryTransaction
        {
            Memo = "Balance assertion",
            AccountId = Guid.NewGuid(),
            AssertedCommodityId = cad,
            AssertedQuantity = 100m,
            Splits = [Split(cad, 100m)],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("exactly two splits", ex.Message);
    }

    [Fact]
    public async Task BalanceEntry_SplitNotInAssertedCommodity_Throws()
    {
        var cad = Guid.NewGuid();
        var usd = Guid.NewGuid();
        var account = Guid.NewGuid();
        var adjustments = Guid.NewGuid();

        var txn = new BalanceEntryTransaction
        {
            Memo = "Balance assertion",
            AccountId = account,
            AssertedCommodityId = cad,
            AssertedQuantity = 100m,
            Splits =
            [
                new() { Id = Guid.NewGuid(), AccountId = account, CommodityId = usd, Quantity = 100m },
                new() { Id = Guid.NewGuid(), AccountId = adjustments, CommodityId = cad, Quantity = -100m },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("asserted commodity", ex.Message);
    }

    [Fact]
    public async Task BalanceEntry_SplitWithAnnotation_Throws()
    {
        var cad = Guid.NewGuid();
        var usd = Guid.NewGuid();
        var account = Guid.NewGuid();
        var adjustments = Guid.NewGuid();

        var txn = new BalanceEntryTransaction
        {
            Memo = "Balance assertion",
            AccountId = account,
            AssertedCommodityId = cad,
            AssertedQuantity = 100m,
            Splits =
            [
                new()
                {
                    Id = Guid.NewGuid(),
                    AccountId = account,
                    CommodityId = cad,
                    Quantity = 100m,
                    UnitPriceRate = 1m,
                    UnitPriceCommodityId = usd,
                },
                new() { Id = Guid.NewGuid(), AccountId = adjustments, CommodityId = cad, Quantity = -100m },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("price annotation", ex.Message);
    }

    [Fact]
    public async Task BalanceEntry_NoSplitOnAssertedAccount_Throws()
    {
        var cad = Guid.NewGuid();
        var assertedAccount = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        var adjustments = Guid.NewGuid();

        var txn = new BalanceEntryTransaction
        {
            Memo = "Balance assertion",
            AccountId = assertedAccount,
            AssertedCommodityId = cad,
            AssertedQuantity = 100m,
            Splits =
            [
                new() { Id = Guid.NewGuid(), AccountId = elsewhere, CommodityId = cad, Quantity = 50m },
                new() { Id = Guid.NewGuid(), AccountId = adjustments, CommodityId = cad, Quantity = -50m },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("asserted account", ex.Message);
    }

    [Fact]
    public async Task BalanceEntry_UnbalancedDeltaSplits_Throws()
    {
        var cad = Guid.NewGuid();
        var account = Guid.NewGuid();
        var adjustments = Guid.NewGuid();

        var txn = new BalanceEntryTransaction
        {
            Memo = "Balance assertion",
            AccountId = account,
            AssertedCommodityId = cad,
            AssertedQuantity = 100m,
            Splits =
            [
                new() { Id = Guid.NewGuid(), AccountId = account, CommodityId = cad, Quantity = 100m },
                new() { Id = Guid.NewGuid(), AccountId = adjustments, CommodityId = cad, Quantity = -50m },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Validate(txn));
        Assert.Contains("net to zero", ex.Message);
    }
}
