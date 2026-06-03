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
        NormalTransaction n => n.NormalShape switch
        {
            NormalShape.Transfer => TransactionShape.Transfer,
            NormalShape.InvestmentBuy => TransactionShape.InvestmentBuy,
            NormalShape.InvestmentSell => TransactionShape.InvestmentSell,
            NormalShape.Dividend => TransactionShape.Dividend,
            _ => TransactionShape.Other,
        },
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
            NormalTransaction normal when shape is TransactionShape.InvestmentBuy
                                              or TransactionShape.InvestmentSell =>
                BuildTradeDetails(normal),
            NormalTransaction normal when shape == TransactionShape.Dividend =>
                BuildDividendDetails(normal),
            _ => null,
        };

    // The detail builders degrade to null on a structurally-broken row rather than throwing: a
    // single malformed transaction must not 500 an entire activity list. The row still surfaces
    // with its shape; only its (best-effort) details are absent.
    private static TransferDetails? BuildTransferDetails(NormalTransaction transfer)
    {
        if (transfer.Splits.Count != 2)
            return null;

        // TransferService writes the source split as -amount and the destination as +amount
        // regardless of account type, so the negative-quantity split is always the source.
        var source = transfer.Splits.FirstOrDefault(s => s.Quantity < 0m);
        if (source is null)
            return null;
        var destination = transfer.Splits.FirstOrDefault(s => s.Id != source.Id);
        if (destination is null)
            return null;

        return new TransferDetails(
            source.AccountId,
            destination.AccountId,
            source.CommodityId,
            destination.CommodityId,
            Math.Abs(source.Quantity),
            Math.Abs(destination.Quantity));
    }

    private static TradeDetails? BuildTradeDetails(NormalTransaction trade)
    {
        // The security leg is the annotated split (carries the per-unit price); the funding
        // currency is its annotation commodity. Both legs sit on the same investment account.
        var security = trade.Splits.FirstOrDefault(
            s => s.UnitPriceRate is not null && s.UnitPriceCommodityId is not null);
        if (security is null)
            return null;

        return new TradeDetails(
            security.AccountId,
            security.CommodityId,
            security.UnitPriceCommodityId!.Value,
            Math.Abs(security.Quantity),
            security.UnitPriceRate!.Value);
    }

    private static DividendDetails? BuildDividendDetails(NormalTransaction dividend)
    {
        // Both legs are in the payment currency: the investment-account leg is positive (cash in),
        // the system Income leg is negative. Amount is the positive investment leg — safe because
        // the validator guarantees Amount > 0, so the positive leg is unambiguously the account leg.
        var account = dividend.Splits.FirstOrDefault(s => s.Quantity > 0m);
        if (account is null)
            return null;

        return new DividendDetails(
            account.AccountId,
            account.CommodityId,
            account.Quantity);
    }
}
