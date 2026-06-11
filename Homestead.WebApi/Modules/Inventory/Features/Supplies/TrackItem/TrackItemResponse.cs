using Homestead.WebApi.Modules.Inventory.Features.Items;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.TrackItem;

/// <summary>
/// Result of a track-one. <see cref="Detail"/> is the refreshed supply detail so the page updates in
/// one round-trip (like every other stock op); <see cref="Item"/> is the freshly-created individual
/// item, enriched for the success toast without a second call.
/// </summary>
public record TrackItemResponse
{
    public required SupplyDetailResponse Detail { get; init; }
    public required ItemResponse Item { get; init; }
}
