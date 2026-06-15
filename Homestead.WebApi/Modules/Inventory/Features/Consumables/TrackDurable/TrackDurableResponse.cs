using Homestead.WebApi.Modules.Inventory.Features.Durables;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

/// <summary>
/// Result of a track-one. <see cref="Detail"/> is the refreshed consumable detail so the page updates in
/// one round-trip (like every other stock op); <see cref="Item"/> is the freshly-created individual
/// item, enriched for the success toast without a second call.
/// </summary>
public record TrackDurableResponse
{
    public required ConsumableDetailResponse Detail { get; init; }
    public required DurableResponse Item { get; init; }
}
