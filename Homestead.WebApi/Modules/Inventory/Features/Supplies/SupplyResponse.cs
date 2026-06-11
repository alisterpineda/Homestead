using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>
/// A fungible item ("supply") — a type of consumable, with its total on-hand summed across all
/// stock entries and a derived low-stock flag. Per-location breakdown lives in
/// <see cref="SupplyDetailResponse"/>; the movement ledger has its own endpoint.
/// </summary>
public record SupplyResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required ItemStatus Status { get; init; }
    public string? Notes { get; init; }

    /// <summary>Unit of measure ("screws", "rolls", "gal").</summary>
    public required string Unit { get; init; }

    /// <summary>Optional global low-stock trigger, compared against <see cref="TotalOnHand"/>.</summary>
    public decimal? RestockThreshold { get; init; }

    /// <summary>Sum of quantity across every stock entry; zero when nothing is on hand.</summary>
    public required decimal TotalOnHand { get; init; }

    /// <summary>True when a threshold is set and the total is at or below it.</summary>
    public required bool IsLow { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
