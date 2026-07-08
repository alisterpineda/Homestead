using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables;

/// <summary>
/// A fungible item ("consumable") — a type of consumable, with its total on-hand summed across all
/// stock entries and a derived low-stock flag. The per-location breakdown is attached on list
/// reads (<see cref="StockLocations"/>) and on the detail read via
/// <see cref="ConsumableDetailResponse"/>; the movement ledger has its own endpoint.
/// </summary>
public record ConsumableResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required ItemStatus Status { get; init; }
    public string? Notes { get; init; }

    /// <summary>Unit of measure ("screws", "rolls", "gal"). Null when the consumable has no unit;
    /// the frontend renders a generic fallback word at display time.</summary>
    public string? Unit { get; init; }

    /// <summary>Optional global low-stock trigger, compared against <see cref="TotalOnHand"/>.</summary>
    public decimal? RestockThreshold { get; init; }

    /// <summary>Canonical brand id, or null when unattributed.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>Brand name, denormalised for display; null when unattributed.</summary>
    public string? BrandName { get; init; }

    /// <summary>Sum of quantity across every stock entry; zero when nothing is on hand.</summary>
    public required decimal TotalOnHand { get; init; }

    /// <summary>True when a threshold is set and the total is at or below it.</summary>
    public required bool IsLow { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Resolved per-location breakdown. Filled by the list read only — create/patch reload
    /// responses leave it empty because their consumers don't render locations.
    /// </summary>
    public IReadOnlyList<StockEntryResponse> StockLocations { get; init; } = [];
}
