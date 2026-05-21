using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions;

internal static class TransactionMapping
{
    public static TransactionResponse ToResponse(AccountTransaction transaction)
    {
        var shape = ResolveShape(transaction);
        var splits = transaction.Splits
            .Select(s => new TransactionSplitResponse
            {
                Id = s.Id,
                AccountId = s.AccountId,
                CommodityId = s.CommodityId,
                Quantity = s.Quantity,
                UnitPriceRate = s.UnitPriceRate,
                UnitPriceCommodityId = s.UnitPriceCommodityId,
                Memo = s.Memo,
            })
            .ToList();

        return new TransactionResponse
        {
            Id = transaction.Id,
            Shape = shape,
            OccurredAt = transaction.OccurredAt,
            Memo = transaction.Memo,
            RelatedTransactionId = transaction.RelatedTransactionId,
            Splits = splits,
            Details = ResolveDetails(transaction, shape),
        };
    }

    public static TransactionShape ResolveShape(AccountTransaction transaction) => transaction switch
    {
        BalanceEntryTransaction => TransactionShape.BalanceEntry,
        OpeningBalanceTransaction => TransactionShape.OpeningBalance,
        NormalTransaction { Splits.Count: 2 } => TransactionShape.Transfer,
        _ => TransactionShape.Other,
    };

    private static TransactionDetails? ResolveDetails(AccountTransaction transaction, TransactionShape shape)
        => transaction switch
        {
            BalanceEntryTransaction be => new BalanceEntryDetails(
                be.AccountId,
                be.AssertedCommodityId,
                be.AssertedQuantity),
            NormalTransaction normal when shape == TransactionShape.Transfer =>
                BuildTransferDetails(normal),
            _ => null,
        };

    private static TransferDetails BuildTransferDetails(NormalTransaction transfer)
    {
        if (transfer.Splits.Count != 2)
            throw new InvalidOperationException("Transfer must have exactly two splits.");

        // TransferService writes the source split as -amount and the destination as +amount
        // regardless of account type, so the negative-quantity split is always the source.
        var splits = transfer.Splits.ToList();
        var source = splits.First(s => s.Quantity < 0m);
        var destination = splits.First(s => s.Id != source.Id);

        return new TransferDetails(
            source.AccountId,
            destination.AccountId,
            source.CommodityId,
            destination.CommodityId,
            Math.Abs(source.Quantity),
            Math.Abs(destination.Quantity));
    }
}
