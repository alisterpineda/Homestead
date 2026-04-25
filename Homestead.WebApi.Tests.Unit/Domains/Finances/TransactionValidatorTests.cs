using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class TransactionValidatorTests
{
    private readonly TransactionValidator _sut = new();

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
    public void Normal_SameCommodityBalancedSplits_PassesValidation()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Description = "Transfer",
            Splits = [Split(cad, 100m), Split(cad, -100m)],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void Normal_UnbalancedSplits_ThrowsBusinessRule()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Description = "Bad transfer",
            Splits = [Split(cad, 100m), Split(cad, -50m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("per commodity", ex.Message);
    }

    [Fact]
    public void Normal_SingleSplit_ThrowsBusinessRule()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Description = "Incomplete",
            Splits = [Split(cad, 100m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("at least two splits", ex.Message);
    }

    [Fact]
    public void Normal_CrossCurrencyPricedTransfer_BalancesViaAnnotation()
    {
        // USD chequing → CAD chequing: -100 USD @ 1.35 CAD ↔ +135 CAD
        var usd = Guid.NewGuid();
        var cad = Guid.NewGuid();

        var txn = new NormalTransaction
        {
            Description = "Cross-currency transfer",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 1.35m, unitPriceCommodityId: cad),
                Split(cad, 135m),
            ],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void Normal_CrossCurrencyPricedTransfer_Unbalanced_Throws()
    {
        var usd = Guid.NewGuid();
        var cad = Guid.NewGuid();

        var txn = new NormalTransaction
        {
            Description = "Bad cross-currency transfer",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 1.35m, unitPriceCommodityId: cad),
                Split(cad, 140m),
            ],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("per commodity", ex.Message);
    }

    [Fact]
    public void Normal_InvestmentBuy_SecurityLegAnnotatedInFundingCurrency_Balances()
    {
        var cad = Guid.NewGuid();
        var veqt = Guid.NewGuid();

        var txn = new NormalTransaction
        {
            Description = "Buy 2 VEQT @ 50",
            Splits =
            [
                Split(cad, -100m),
                Split(veqt, 2m, unitPriceRate: 50m, unitPriceCommodityId: cad),
            ],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void Normal_Annotation_MissingCommodity_Throws()
    {
        var cad = Guid.NewGuid();
        var usd = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Description = "Invalid",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 1.35m),
                Split(cad, 135m),
            ],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("both rate and commodity", ex.Message);
    }

    [Fact]
    public void Normal_Annotation_SelfQuote_Throws()
    {
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Description = "Invalid",
            Splits =
            [
                Split(cad, -100m, unitPriceRate: 1m, unitPriceCommodityId: cad),
                Split(cad, 100m),
            ],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public void Normal_Annotation_ZeroRate_Throws()
    {
        var usd = Guid.NewGuid();
        var cad = Guid.NewGuid();
        var txn = new NormalTransaction
        {
            Description = "Invalid",
            Splits =
            [
                Split(usd, -100m, unitPriceRate: 0m, unitPriceCommodityId: cad),
                Split(cad, 0m),
            ],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public void StockSplit_SingleSplit_Passes()
    {
        var aapl = Guid.NewGuid();
        var txn = new StockSplitTransaction
        {
            Description = "2-for-1",
            Splits = [Split(aapl, 100m)],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void StockSplit_MultipleAccountsSameCommodity_Passes()
    {
        var aapl = Guid.NewGuid();
        var txn = new StockSplitTransaction
        {
            Description = "AAPL 2-for-1",
            Splits = [Split(aapl, 10m), Split(aapl, 5m)],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void StockSplit_MixedCommodities_Throws()
    {
        var aapl = Guid.NewGuid();
        var msft = Guid.NewGuid();
        var txn = new StockSplitTransaction
        {
            Description = "Invalid",
            Splits = [Split(aapl, 10m), Split(msft, 5m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("same commodity", ex.Message);
    }

    [Fact]
    public void StockSplit_WithAnnotation_Throws()
    {
        var aapl = Guid.NewGuid();
        var cad = Guid.NewGuid();
        var txn = new StockSplitTransaction
        {
            Description = "Invalid",
            Splits = [Split(aapl, 10m, unitPriceRate: 100m, unitPriceCommodityId: cad)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("not carry a price annotation", ex.Message);
    }

    [Fact]
    public void StockSplit_ZeroQuantity_Throws()
    {
        var aapl = Guid.NewGuid();
        var txn = new StockSplitTransaction
        {
            Description = "Invalid",
            Splits = [Split(aapl, 0m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("non-zero quantity", ex.Message);
    }

    [Fact]
    public void BalanceEntry_Throws_NotImplementedException_UntilFollowUp()
    {
        var txn = new BalanceEntryTransaction
        {
            Description = "January close",
            AccountId = Guid.NewGuid(),
            AssertedCommodityId = Guid.NewGuid(),
            AssertedQuantity = 100m,
        };

        Assert.Throws<NotImplementedException>(() => _sut.Validate(txn));
    }
}
