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
            Memo = "Transfer",
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
            Memo = "Bad transfer",
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
            Memo = "Incomplete",
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
            Memo = "Cross-currency transfer",
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
            Memo = "Bad cross-currency transfer",
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
            Memo = "Buy 2 VEQT @ 50",
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
            Memo = "Invalid",
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
            Memo = "Invalid",
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
            Memo = "Invalid",
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
    public void BalanceEntry_TwoSameCommodityUnpricedSplitsOnAccountAndCounterpart_Passes()
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

        _sut.Validate(txn);
    }

    [Fact]
    public void BalanceEntry_WrongSplitCount_Throws()
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

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("exactly two splits", ex.Message);
    }

    [Fact]
    public void BalanceEntry_SplitNotInAssertedCommodity_Throws()
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

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("asserted commodity", ex.Message);
    }

    [Fact]
    public void BalanceEntry_SplitWithAnnotation_Throws()
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

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("price annotation", ex.Message);
    }

    [Fact]
    public void BalanceEntry_NoSplitOnAssertedAccount_Throws()
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

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("asserted account", ex.Message);
    }

    [Fact]
    public void BalanceEntry_UnbalancedDeltaSplits_Throws()
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

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("net to zero", ex.Message);
    }
}
