using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;

namespace Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;

/// <summary>
/// The direct contents of one place (an area, a container, or Unplaced) for the overview's
/// in-place drill-down browser, plus the breadcrumb path.
/// </summary>
public record PlaceContentsResponse
{
    /// <summary>
    /// Breadcrumb path including this place as the last segment; empty for the Unplaced scope.
    /// </summary>
    public required IReadOnlyList<LocationSegmentResponse> Path { get; init; }

    /// <summary>
    /// The drilled container itself, enriched like a list row (drives the header's "details"
    /// quick-panel button); null for area and Unplaced scopes.
    /// </summary>
    public DurableResponse? ContainerItem { get; init; }

    /// <summary>
    /// Low-stock consumable count for this place (0 for Unplaced), globally-low consumables (NOT a
    /// per-location threshold): the resolved subtree for an AREA scope, the container's DIRECT contents
    /// for a CONTAINER scope. Drives the Browse drilled low-stock warning panel (count-only).
    /// </summary>
    public required int ScopedLowStockCount { get; init; }

    /// <summary>
    /// Lent-item count for this place (0 for Unplaced): the resolved subtree for an AREA scope, the
    /// container's DIRECT lent children (durables and child containers alike) for a CONTAINER scope.
    /// Drives the Browse drilled lent warning panel (count-only).
    /// </summary>
    public required int ScopedLentCount { get; init; }

    /// <summary>
    /// Direct child areas (area scope only; always empty for container/Unplaced scopes), name-ordered.
    /// These render as drill cards alongside containers; each carries its own rolled subtree count.
    /// </summary>
    public required IReadOnlyList<PlaceChildAreaSummary> ChildAreas { get; init; }

    /// <summary>Direct live child containers, name-ordered.</summary>
    public required IReadOnlyList<PlaceContainerSummary> Containers { get; init; }

    /// <summary>Direct live non-container items, name-ordered and enriched.</summary>
    public required IReadOnlyList<DurableResponse> Items { get; init; }

    /// <summary>
    /// Live consumables with a direct stock entry at this place, name-ordered. Always empty for the
    /// Unplaced scope by construction — a stock entry always has a location (an area or a
    /// container), so no consumable can sit directly nowhere.
    /// </summary>
    public required IReadOnlyList<PlaceConsumableResponse> Consumables { get; init; }
}

/// <summary>A drillable child-area card: the area's rolled subtree counts (same semantics as the overview cards).</summary>
public record PlaceChildAreaSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Rolled resolved count for the child area (its own contents plus its descendants').</summary>
    public required int ItemCount { get; init; }

    /// <summary>Distinct low-stock consumables resolving anywhere inside the child area's subtree.</summary>
    public required int LowStockCount { get; init; }
}

/// <summary>A drillable child-container card: resolved subtree stats plus a contents preview.</summary>
public record PlaceContainerSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required ItemStatus Status { get; init; }

    /// <summary>Resolved subtree count (everything inside, however deep) — matches the area cards' semantics.</summary>
    public required int ItemCount { get; init; }

    /// <summary>Distinct low-stock consumables with stock anywhere inside.</summary>
    public required int LowStockCount { get; init; }

    /// <summary>Up to 3 name-sorted direct contents (child items + consumables stocked in it).</summary>
    public required IReadOnlyList<string> PreviewNames { get; init; }
}

/// <summary>One consumable row in a drilled place: the consumable plus how much of it sits right here.</summary>
public record PlaceConsumableResponse
{
    /// <summary>The consumable with its full <see cref="ConsumableResponse.StockLocations"/> breakdown (GetConsumables parity).</summary>
    public required ConsumableResponse Consumable { get; init; }

    /// <summary>Quantity of the stock entry at this place (one entry per location by construction).</summary>
    public required decimal LocalQuantity { get; init; }
}
