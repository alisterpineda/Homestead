using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances;

internal static class OpeningBalanceMapping
{
    public static OpeningBalanceResponse ToResponse(
        AccountTransaction transaction,
        AccountTransactionSplit accountSplit,
        Commodity commodity,
        decimal? existingSecurityRate = null)
    {
        var quantity = Math.Abs(accountSplit.Quantity);
        var value = Math.Abs(accountSplit.Value);

        decimal? rate = null;
        if (commodity.Kind == CommodityKind.Security && quantity != 0m)
            rate = existingSecurityRate ?? Math.Abs(accountSplit.Value / accountSplit.Quantity);

        return new OpeningBalanceResponse
        {
            TransactionId = transaction.Id,
            AccountId = accountSplit.AccountId,
            CommodityId = accountSplit.CommodityId,
            Quantity = quantity,
            Value = value,
            ValuePerUnit = rate,
            AsOfDate = transaction.TxnDate,
        };
    }

    public static AccountTransactionSplit AccountSideSplit(
        AccountTransaction transaction,
        Guid openingBalancesSystemAccountId)
    {
        if (transaction.Splits.Count != 2)
            throw new BusinessRuleException("Opening-balance transaction must have exactly two splits.");

        return transaction.Splits.First(s => s.AccountId != openingBalancesSystemAccountId);
    }
}
