namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances;

public record OpeningBalanceResponse
{
    public required Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// User-facing quantity (always positive). Internal sign conventions are hidden from clients.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// Per-unit price captured for this opening balance. Null for currencies (rate is always 1)
    /// and for unpriced security positions.
    /// </summary>
    public decimal? ValuePerUnit { get; init; }

    public required DateOnly AsOfDate { get; init; }
}
