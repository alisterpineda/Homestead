using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// Resolved semantics: an item counts toward the area its container chain reaches (broken or
/// cyclic chain ⇒ unplaced); a fungible type counts once per area any of its stock entries
/// resolves to. Counts then roll up the area-parent chain — Bedroom's contents also count toward
/// Basement and House. This mirrors the parent-chain walk in <see cref="ItemLocationResolver"/>
/// (both the container climb and the area-ancestor climb), which must match what the area-filtered
/// items list shows; keep the two in sync.
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

        // Directly-placed live non-fungible items need no walk — tally in SQL. Leaf-keyed (by the
        // area the item literally sits in); the ancestor roll-up happens once at the end.
        var areaItemCounts = await db.Items.AsNoTracking()
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
                areaItemCounts[resolved] = areaItemCounts.GetValueOrDefault(resolved) + 1;
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

        // Roll leaf-keyed tallies up the area-parent chain: contents of Bedroom also count toward
        // Basement and House. Items are additive; supply/low TYPE sets are union'd so the same
        // supply type stocked in two sub-areas counts once for a shared ancestor (the dedup is
        // load-bearing — it keeps the rolled card count equal to what the area-filtered list shows).
        // Mirrors the container subtree attribution above, applied to the area's own ancestors.
        var areaParents = await db.Areas.AsNoTracking()
            .Select(a => new { a.Id, a.ParentAreaId })
            .ToDictionaryAsync(a => a.Id, a => a.ParentAreaId, ct);

        var rolledItems = new Dictionary<Guid, int>();
        foreach (var (areaId, count) in areaItemCounts)
            foreach (var ancestor in AncestorsInclusive(areaId, areaParents))
                rolledItems[ancestor] = rolledItems.GetValueOrDefault(ancestor) + count;

        var rolledSupplyTypes = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (areaId, types) in areaSupplyTypes)
            foreach (var ancestor in AncestorsInclusive(areaId, areaParents))
                UnionTypes(rolledSupplyTypes, ancestor, types);

        var rolledLowTypes = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (areaId, types) in areaLowTypes)
            foreach (var ancestor in AncestorsInclusive(areaId, areaParents))
                UnionTypes(rolledLowTypes, ancestor, types);

        var areaCounts = new Dictionary<Guid, int>();
        foreach (var areaId in rolledItems.Keys.Union(rolledSupplyTypes.Keys))
            areaCounts[areaId] = rolledItems.GetValueOrDefault(areaId)
                + (rolledSupplyTypes.TryGetValue(areaId, out var supplyTypes) ? supplyTypes.Count : 0);

        // The same count for the area's OWN contents only (no roll-up) — the deduped leaf tally.
        // GetPlaceContents subtracts the direct-card counts from this to get the container-nested
        // figure; using the leaf count (not a sum of rolled child counts) keeps the supply-type
        // dedup intact, so a type shared between an area and its sub-areas isn't subtracted twice.
        var areaOwnCounts = new Dictionary<Guid, int>();
        foreach (var areaId in areaItemCounts.Keys.Union(areaSupplyTypes.Keys))
            areaOwnCounts[areaId] = areaItemCounts.GetValueOrDefault(areaId)
                + (areaSupplyTypes.TryGetValue(areaId, out var ownSupplyTypes) ? ownSupplyTypes.Count : 0);

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
            AreaOwnItemCounts = areaOwnCounts,
            UnplacedItems = unplaced,
            AreaLowStockCounts = rolledLowTypes.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
            ContainerStats = containerStats,
        };
    }

    private static void AddType(Dictionary<Guid, HashSet<Guid>> typesByKey, Guid key, Guid typeId)
    {
        if (!typesByKey.TryGetValue(key, out var types))
            typesByKey[key] = types = [];
        types.Add(typeId);
    }

    private static void UnionTypes(Dictionary<Guid, HashSet<Guid>> typesByKey, Guid key, HashSet<Guid> types)
    {
        if (!typesByKey.TryGetValue(key, out var existing))
            typesByKey[key] = existing = [];
        existing.UnionWith(types);
    }

    /// <summary>
    /// Yields an area plus all of its ancestor areas (leaf→root), cycle-guarded. A missing parent
    /// link (the root, or a deleted area) ends the walk; a cycle is broken by the visited set.
    /// </summary>
    private static IEnumerable<Guid> AncestorsInclusive(
        Guid areaId, IReadOnlyDictionary<Guid, Guid?> areaParents)
    {
        var visited = new HashSet<Guid>();
        Guid? currentId = areaId;
        while (currentId is { } id && visited.Add(id))
        {
            yield return id;
            currentId = areaParents.GetValueOrDefault(id);
        }
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
