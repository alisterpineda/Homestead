using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public class TransactionValidator : ITransactionValidator
{
    public void Validate(AccountTransaction transaction)
    {
        switch (transaction)
        {
            case NormalTransaction:
                ValidateNormal(transaction);
                break;
            case StockSplitTransaction:
                ValidateStockSplit(transaction);
                break;
            case BalanceEntryTransaction:
                throw new NotImplementedException(
                    "BalanceEntryTransaction validation requires the reconciliation service, which is not yet wired.");
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

        var sum = splits.Sum(s => s.Value);
        if (sum != 0m)
            throw new BusinessRuleException(
                $"Transaction splits must sum to zero in the transaction currency (got {sum}).");
    }

    private static void ValidateStockSplit(AccountTransaction transaction)
    {
        var splits = transaction.Splits;

        if (splits.Count != 1)
            throw new BusinessRuleException("A stock-split transaction must have exactly one split.");

        var split = splits.First();
        if (split.Value != 0m)
            throw new BusinessRuleException("A stock-split transaction's split must have value zero.");
        if (split.Quantity == 0m)
            throw new BusinessRuleException("A stock-split transaction's split must have a non-zero quantity.");
    }
}
