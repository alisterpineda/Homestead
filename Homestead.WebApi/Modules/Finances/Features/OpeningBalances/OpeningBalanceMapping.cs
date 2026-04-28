using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances;

internal static class OpeningBalanceMapping
{
    public static OpeningBalanceResponse ToResponse(
        AccountTransaction transaction,
        AccountTransactionSplit accountSplit)
    {
        return new OpeningBalanceResponse
        {
            TransactionId = transaction.Id,
            AccountId = accountSplit.AccountId,
            CommodityId = accountSplit.CommodityId,
            Description = transaction.Description,
            Quantity = Math.Abs(accountSplit.Quantity),
            ValuePerUnit = accountSplit.UnitPriceRate,
            AsOfDate = DateOnly.FromDateTime(transaction.OccurredAt.UtcDateTime),
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
