namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// Computes household-wide resolved placement in one pass: per-area counts that match what the
/// area-filtered items list shows, plus the resolved-unplaced items. Shared by the inventory
/// overview and the areas list so card, table, and click-through list always agree.
/// </summary>
public interface IInventoryPlacementService
{
    Task<InventoryPlacement> ComputeAsync(CancellationToken ct = default);
}

public record InventoryPlacement
{
    /// <summary>
    /// areaId → count of live rows the area-filtered list shows: non-fungible live items resolving
    /// to the area (directly placed + via container chain) + distinct fungible types with at least
    /// one stock entry resolving there. Areas with no rows are absent.
    /// </summary>
    public required IReadOnlyDictionary<Guid, int> AreaItemCounts { get; init; }

    /// <summary>Live non-fungible items whose container chain reaches no area (resolved-unplaced).</summary>
    public required IReadOnlyList<UnplacedItemInfo> UnplacedItems { get; init; }
}

public readonly record struct UnplacedItemInfo(Guid Id, string Name);
