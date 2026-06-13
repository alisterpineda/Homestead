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

    /// <summary>
    /// areaId → distinct low-stock supplies (Active, threshold set, total at or below it) with at
    /// least one stock entry resolving to the area. Areas with none are absent.
    /// </summary>
    public required IReadOnlyDictionary<Guid, int> AreaLowStockCounts { get; init; }

    /// <summary>
    /// containerId → resolved subtree stats, attributed upward: a nested item counts toward every
    /// container in its chain, a supply once per container its entries sit in (the holding
    /// container included). Containers with empty subtrees are absent.
    /// </summary>
    public required IReadOnlyDictionary<Guid, ContainerStats> ContainerStats { get; init; }
}

public readonly record struct UnplacedItemInfo(Guid Id, string Name);

/// <summary>
/// Subtree stats for one container, mirroring <see cref="InventoryPlacement.AreaItemCounts"/>
/// semantics: live non-fungible items anywhere below it + distinct live supplies with stock
/// anywhere below it (or in it), and the low-stock subset of those supplies.
/// </summary>
public readonly record struct ContainerStats(int ResolvedContentsCount, int LowStockCount);
