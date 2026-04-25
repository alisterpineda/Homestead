namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries;

public record BalanceEntryResponse
{
    public required Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }

    /// <summary>User-facing asserted amount (always positive). Server applies the account-type sign flip internally.</summary>
    public required decimal AssertedAmount { get; init; }

    public required DateOnly Date { get; init; }
    public string? Note { get; init; }
}
