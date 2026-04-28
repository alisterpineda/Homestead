using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public class TransactionValidator : ITransactionValidator
{
    public void Validate(AccountTransaction transaction)
    {
        switch (transaction)
        {
            case BalanceEntryTransaction be:
                ValidateBalanceEntry(be);
                break;
            case StockSplitTransaction:
                ValidateStockSplit(transaction);
                break;
            case OpeningBalanceTransaction:
            case NormalTransaction:
                ValidateNormal(transaction);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown transaction type: {transaction.GetType().Name}");
        }
    }

    private static void ValidateNormal(AccountTransaction transaction)
    {
        var splits = transaction.Splits;

        if (splits.Count < 2)
            throw new BusinessRuleException("A transaction must have at least two splits.");

        foreach (var split in splits)
            ValidateAnnotationShape(split);

        // Per-commodity weight balance: each split contributes one weight to exactly one commodity
        // (its own commodity when unpriced, or the annotation commodity when priced).
        var weights = new Dictionary<Guid, decimal>();
        foreach (var split in splits)
        {
            var (bucket, weight) = ResolveWeight(split);
            weights.TryGetValue(bucket, out var running);
            weights[bucket] = running + weight;
        }

        foreach (var (commodityId, sum) in weights)
        {
            if (sum != 0m)
                throw new BusinessRuleException(
                    $"Transaction splits must balance per commodity (commodity {commodityId} sums to {sum}).");
        }
    }

    private static void ValidateStockSplit(AccountTransaction transaction)
    {
        var splits = transaction.Splits;

        if (splits.Count < 1)
            throw new BusinessRuleException("A stock-split transaction must have at least one split.");

        var commodityId = splits.First().CommodityId;
        foreach (var split in splits)
        {
            if (split.CommodityId != commodityId)
                throw new BusinessRuleException(
                    "A stock-split transaction's splits must all reference the same commodity.");
            if (split.Quantity == 0m)
                throw new BusinessRuleException("A stock-split transaction's splits must have a non-zero quantity.");
            if (split.UnitPriceRate is not null || split.UnitPriceCommodityId is not null)
                throw new BusinessRuleException("A stock-split transaction's splits must not carry a price annotation.");
        }
    }

    private static void ValidateBalanceEntry(BalanceEntryTransaction entry)
    {
        var splits = entry.Splits;

        if (splits.Count != 2)
            throw new BusinessRuleException("A balance entry must have exactly two splits.");

        foreach (var split in splits)
        {
            if (split.CommodityId != entry.AssertedCommodityId)
                throw new BusinessRuleException(
                    "Both splits of a balance entry must be in the asserted commodity.");
            if (split.UnitPriceRate is not null || split.UnitPriceCommodityId is not null)
                throw new BusinessRuleException("Balance-entry splits cannot carry a price annotation.");
        }

        if (splits.Count(s => s.AccountId == entry.AccountId) != 1)
            throw new BusinessRuleException(
                "A balance entry must have exactly one split on the asserted account.");

        // Same-commodity weight balance: split quantities must net to zero.
        var sum = splits.Sum(s => s.Quantity);
        if (sum != 0m)
            throw new BusinessRuleException(
                $"Balance-entry splits must net to zero in the asserted commodity (sum: {sum}).");
    }

    private static void ValidateAnnotationShape(AccountTransactionSplit split)
    {
        var hasRate = split.UnitPriceRate is not null;
        var hasCommodity = split.UnitPriceCommodityId is not null;

        if (hasRate != hasCommodity)
            throw new BusinessRuleException(
                "A split's unit-price annotation must have both rate and commodity, or neither.");

        if (!hasRate) return;

        if (split.UnitPriceRate!.Value <= 0m)
            throw new BusinessRuleException("A split's unit-price rate must be greater than zero.");
        if (split.UnitPriceCommodityId!.Value == split.CommodityId)
            throw new BusinessRuleException(
                "A split's unit-price annotation commodity must differ from the split's commodity.");
    }

    private static (Guid bucket, decimal weight) ResolveWeight(AccountTransactionSplit split)
    {
        if (split.UnitPriceRate is decimal rate && split.UnitPriceCommodityId is Guid quote)
            return (quote, split.Quantity * rate);
        return (split.CommodityId, split.Quantity);
    }
}
