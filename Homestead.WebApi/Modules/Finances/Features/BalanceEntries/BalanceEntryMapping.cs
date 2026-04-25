using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries;

internal static class BalanceEntryMapping
{
    public static BalanceEntryResponse ToResponse(BalanceEntryTransaction entry) => new()
    {
        TransactionId = entry.Id,
        AccountId = entry.AccountId,
        CommodityId = entry.AssertedCommodityId,
        AssertedAmount = Math.Abs(entry.AssertedQuantity),
        Date = DateOnly.FromDateTime(entry.OccurredAt.UtcDateTime),
        Note = entry.Note,
    };
}
