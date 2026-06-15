namespace Homestead.WebApi.Modules.Inventory.Features.Consumables;

/// <summary>
/// A consumable plus its per-location stock breakdown. The movement ledger is paged separately (it can
/// grow without bound) via the movements endpoint.
/// </summary>
public record ConsumableDetailResponse
{
    public required ConsumableResponse Consumable { get; init; }

    /// <summary>Per-location stock entries, highest quantity first. Empty when nothing is on hand.</summary>
    public required IReadOnlyList<StockEntryResponse> Locations { get; init; }
}
