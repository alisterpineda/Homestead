using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class TransactionValidatorTests
{
    private readonly TransactionValidator _sut = new();

    private static AccountTransactionSplit Split(decimal quantity, decimal value)
        => new()
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            CommodityId = Guid.NewGuid(),
            Quantity = quantity,
            Value = value,
        };

    [Fact]
    public void Normal_BalancedSplits_PassesValidation()
    {
        var txn = new NormalTransaction
        {
            Description = "Transfer",
            Splits = [Split(100m, 100m), Split(-100m, -100m)],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void Normal_UnbalancedSplits_ThrowsBusinessRule()
    {
        var txn = new NormalTransaction
        {
            Description = "Bad transfer",
            Splits = [Split(100m, 100m), Split(-50m, -50m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("sum to zero", ex.Message);
    }

    [Fact]
    public void Normal_SingleSplit_ThrowsBusinessRule()
    {
        var txn = new NormalTransaction
        {
            Description = "Incomplete",
            Splits = [Split(100m, 100m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("at least two splits", ex.Message);
    }

    [Fact]
    public void StockSplit_OneSplit_ZeroValue_NonZeroQuantity_Passes()
    {
        var txn = new StockSplitTransaction
        {
            Description = "2-for-1",
            Splits = [Split(100m, 0m)],
        };

        _sut.Validate(txn);
    }

    [Fact]
    public void StockSplit_NonZeroValue_Throws()
    {
        var txn = new StockSplitTransaction
        {
            Description = "Invalid",
            Splits = [Split(100m, 1m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("value zero", ex.Message);
    }

    [Fact]
    public void StockSplit_ZeroQuantity_Throws()
    {
        var txn = new StockSplitTransaction
        {
            Description = "Invalid",
            Splits = [Split(0m, 0m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("non-zero quantity", ex.Message);
    }

    [Fact]
    public void StockSplit_MultipleSplits_Throws()
    {
        var txn = new StockSplitTransaction
        {
            Description = "Invalid",
            Splits = [Split(100m, 0m), Split(50m, 0m)],
        };

        var ex = Assert.Throws<BusinessRuleException>(() => _sut.Validate(txn));
        Assert.Contains("exactly one split", ex.Message);
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
