using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Modules.Inventory.Features.Supplies;

namespace Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;

/// <summary>
/// The direct contents of one place (an area, a container, or Unplaced) for the overview's
/// in-place drill-down browser, plus the breadcrumb path and the direct/nested count split.
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
    public ItemResponse? ContainerItem { get; init; }

    /// <summary>
    /// Live things directly at this place: child containers + non-container items + distinct
    /// supplies with a direct stock entry here.
    /// </summary>
    public required int DirectItemCount { get; init; }

    /// <summary>
    /// Things deeper inside this place's containers: the resolved subtree total (same semantics
    /// as the overview cards' counts) minus <see cref="DirectItemCount"/>, floored at zero.
    /// </summary>
    public required int NestedItemCount { get; init; }

    /// <summary>
    /// Direct child areas (area scope only; always empty for container/Unplaced scopes), name-ordered.
    /// These render as drill cards alongside containers; each carries its own rolled subtree count.
    /// </summary>
    public required IReadOnlyList<PlaceChildAreaSummary> ChildAreas { get; init; }

    /// <summary>Direct live child containers, name-ordered.</summary>
    public required IReadOnlyList<PlaceContainerSummary> Containers { get; init; }

    /// <summary>Direct live non-container items, name-ordered and enriched.</summary>
    public required IReadOnlyList<ItemResponse> Items { get; init; }

    /// <summary>
    /// Live supplies with a direct stock entry at this place, name-ordered. Always empty for the
    /// Unplaced scope by construction — a stock entry always has a location (an area or a
    /// container), so no supply can sit directly nowhere.
    /// </summary>
    public required IReadOnlyList<PlaceSupplyResponse> Supplies { get; init; }
}

/// <summary>A drillable child-area card: the area's rolled subtree counts (same semantics as the overview cards).</summary>
public record PlaceChildAreaSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Rolled resolved count for the child area (its own contents plus its descendants').</summary>
    public required int ItemCount { get; init; }

    /// <summary>Distinct low-stock supplies resolving anywhere inside the child area's subtree.</summary>
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

    /// <summary>Distinct low-stock supplies with stock anywhere inside.</summary>
    public required int LowStockCount { get; init; }

    /// <summary>Up to 3 name-sorted direct contents (child items + supplies stocked in it).</summary>
    public required IReadOnlyList<string> PreviewNames { get; init; }
}

/// <summary>One supply row in a drilled place: the supply plus how much of it sits right here.</summary>
public record PlaceSupplyResponse
{
    /// <summary>The supply with its full <see cref="SupplyResponse.StockLocations"/> breakdown (GetSupplies parity).</summary>
    public required SupplyResponse Supply { get; init; }

    /// <summary>Quantity of the stock entry at this place (one entry per location by construction).</summary>
    public required decimal LocalQuantity { get; init; }
}
