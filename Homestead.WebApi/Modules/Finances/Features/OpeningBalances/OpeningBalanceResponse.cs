namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances;

public record OpeningBalanceResponse
{
    public required Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }

    /// <summary>
    /// User-facing quantity (always positive). Internal sign conventions are hidden from clients.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// User-facing value in the transaction currency (always positive).
    /// </summary>
    public required decimal Value { get; init; }

    /// <summary>
    /// Rate used to convert quantity to value. Null for currency commodities (where rate is always 1).
    /// </summary>
    public decimal? ValuePerUnit { get; init; }

    public required DateOnly AsOfDate { get; init; }
}
