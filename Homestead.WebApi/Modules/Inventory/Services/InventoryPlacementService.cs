using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// Resolved semantics: an item counts toward the area its container chain reaches (broken or
/// cyclic chain ⇒ unplaced); a fungible type counts once per area any of its stock entries
/// resolves to. This mirrors the parent-chain walk in <see cref="ItemLocationResolver"/>, which
/// must match what the area-filtered items list shows; keep the two in sync.
/// </summary>
public class InventoryPlacementService(AppDbContext db) : IInventoryPlacementService
{
    public async Task<InventoryPlacement> ComputeAsync(CancellationToken ct = default)
    {
        // Every valid ancestor is a container; deliberately not status-filtered (a chain may pass
        // through terminal containers), same as the resolver.
        var containers = await db.Items.AsNoTracking()
            .Where(i => i.IsContainer)
            .Select(i => new { i.Id, i.AreaId, i.ParentItemId })
            .ToDictionaryAsync(c => c.Id, c => new ContainerNode(c.AreaId, c.ParentItemId), ct);

        // Directly-placed live non-fungible items need no walk — tally in SQL.
        var areaCounts = await db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible
                && i.AreaId != null
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .GroupBy(i => i.AreaId!.Value)
            .Select(g => new { AreaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AreaId, x => x.Count, ct);

        // Live non-fungible items without a direct area: resolve via the container chain.
        var nested = await db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible
                && i.AreaId == null
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .Select(i => new { i.Id, i.Name, i.ParentItemId })
            .ToListAsync(ct);

        var unplaced = new List<UnplacedItemInfo>();
        var containerItemCounts = new Dictionary<Guid, int>();
        foreach (var item in nested)
        {
            var (areaId, chain) = WalkContainerChain(item.ParentItemId, containers, [item.Id]);
            if (areaId is { } resolved)
                areaCounts[resolved] = areaCounts.GetValueOrDefault(resolved) + 1;
            else
                unplaced.Add(new UnplacedItemInfo(item.Id, item.Name));

            // Upward attribution: the item counts toward every container in its (valid) chain, so
            // a chest's subtree count includes things inside its bins.
            if (chain is not null)
            {
                foreach (var containerId in chain)
                    containerItemCounts[containerId] = containerItemCounts.GetValueOrDefault(containerId) + 1;
            }
        }

        // Low supplies — same predicate as the overview's low-stock tile (Active fungible with a
        // threshold whose summed on-hand is at or below it); archived supplies never count.
        var lowSupplyIds = (await db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.Fungible
                && i.Status == ItemStatus.Active
                && i.RestockThreshold != null
                && i.StockEntries.Sum(e => e.Quantity) <= i.RestockThreshold)
            .Select(i => i.Id)
            .ToListAsync(ct)).ToHashSet();

        // Fungible types count once per area any stock entry resolves to. The status filter is
        // load-bearing: archived supplies can retain stock entries, and the list hides them.
        // Entries resolving to no area are ignored — Unplaced is items-only.
        // A CURRENT entry's holder is always a container — PatchItem's role-off gate blocks
        // turning the role off while stock sits inside — so the IsContainer-only dictionary
        // suffices here; ItemLocationResolver.ResolveHoldingLocationsAsync's by-id tolerance for
        // de-containered holders exists for HISTORICAL ledger locations only.
        var stockEntries = await db.StockEntries.AsNoTracking()
            .Where(e => ItemStatusExtensions.LiveStatuses.Contains(e.Item.Status))
            .Select(e => new { e.ItemId, e.AreaId, e.ContainerItemId })
            .ToListAsync(ct);

        var areaSupplyTypes = new Dictionary<Guid, HashSet<Guid>>();
        var areaLowTypes = new Dictionary<Guid, HashSet<Guid>>();
        var containerSupplyTypes = new Dictionary<Guid, HashSet<Guid>>();
        var containerLowTypes = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var entry in stockEntries)
        {
            // The walk starts AT the holding container (its own AreaId counts, and it belongs to
            // the entry's chain — stock in a bin is inside the bin), unlike an item's walk which
            // starts at its parent.
            var (areaId, chain) = entry.AreaId is { } directAreaId
                ? (directAreaId, (IReadOnlyList<Guid>)[])
                : WalkContainerChain(entry.ContainerItemId, containers, []);

            var isLow = lowSupplyIds.Contains(entry.ItemId);
            if (areaId is { } resolved)
            {
                AddType(areaSupplyTypes, resolved, entry.ItemId);
                if (isLow)
                    AddType(areaLowTypes, resolved, entry.ItemId);
            }

            if (chain is null)
                continue;
            foreach (var containerId in chain)
            {
                // Per-key distinct sets: a supply with stock in two sub-bins counts once for the
                // chest (and once per area), matching the list's one-row-per-type rendering.
                AddType(containerSupplyTypes, containerId, entry.ItemId);
                if (isLow)
                    AddType(containerLowTypes, containerId, entry.ItemId);
            }
        }

        foreach (var (areaId, types) in areaSupplyTypes)
            areaCounts[areaId] = areaCounts.GetValueOrDefault(areaId) + types.Count;

        var containerIds = new HashSet<Guid>(containerItemCounts.Keys);
        containerIds.UnionWith(containerSupplyTypes.Keys);
        var containerStats = new Dictionary<Guid, ContainerStats>(containerIds.Count);
        foreach (var containerId in containerIds)
        {
            containerStats[containerId] = new ContainerStats(
                containerItemCounts.GetValueOrDefault(containerId)
                    + (containerSupplyTypes.TryGetValue(containerId, out var types) ? types.Count : 0),
                containerLowTypes.TryGetValue(containerId, out var low) ? low.Count : 0);
        }

        return new InventoryPlacement
        {
            AreaItemCounts = areaCounts,
            UnplacedItems = unplaced,
            AreaLowStockCounts = areaLowTypes.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
            ContainerStats = containerStats,
        };
    }

    private static void AddType(Dictionary<Guid, HashSet<Guid>> typesByKey, Guid key, Guid typeId)
    {
        if (!typesByKey.TryGetValue(key, out var types))
            typesByKey[key] = types = [];
        types.Add(typeId);
    }

    /// <summary>
    /// Walks the container chain from <paramref name="startId"/> up. Returns the resolved area
    /// (null when the chain ends at an unplaced root) plus the containers visited in order; a
    /// broken or cyclic chain yields a null chain — it attributes to no area and no container.
    /// </summary>
    private static (Guid? AreaId, IReadOnlyList<Guid>? Chain) WalkContainerChain(
        Guid? startId,
        IReadOnlyDictionary<Guid, ContainerNode> containers,
        HashSet<Guid> visited)
    {
        var chain = new List<Guid>();
        var currentId = startId;
        while (currentId is { } id)
        {
            if (!visited.Add(id) || !containers.TryGetValue(id, out var node))
                return (null, null);

            chain.Add(id);
            if (node.AreaId is not null)
                return (node.AreaId, chain);

            currentId = node.ParentItemId;
        }

        return (null, chain);
    }

    private readonly record struct ContainerNode(Guid? AreaId, Guid? ParentItemId);
}
