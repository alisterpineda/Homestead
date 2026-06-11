namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>
/// A supply plus its per-location stock breakdown. The movement ledger is paged separately (it can
/// grow without bound) via the movements endpoint.
/// </summary>
public record SupplyDetailResponse
{
    public required SupplyResponse Supply { get; init; }

    /// <summary>Per-location stock entries, highest quantity first. Empty when nothing is on hand.</summary>
    public required IReadOnlyList<StockEntryResponse> Locations { get; init; }
}
