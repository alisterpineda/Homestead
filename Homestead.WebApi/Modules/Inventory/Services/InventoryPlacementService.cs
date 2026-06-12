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
        foreach (var item in nested)
        {
            var areaId = ResolveAreaViaContainers(item.ParentItemId, containers, [item.Id]);
            if (areaId is { } resolved)
                areaCounts[resolved] = areaCounts.GetValueOrDefault(resolved) + 1;
            else
                unplaced.Add(new UnplacedItemInfo(item.Id, item.Name));
        }

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
        foreach (var entry in stockEntries)
        {
            // The walk starts AT the holding container (its own AreaId counts), unlike an item's
            // walk which starts at its parent.
            var areaId = entry.AreaId
                ?? ResolveAreaViaContainers(entry.ContainerItemId, containers, []);
            if (areaId is { } resolved)
            {
                if (!areaSupplyTypes.TryGetValue(resolved, out var types))
                    areaSupplyTypes[resolved] = types = [];
                types.Add(entry.ItemId);
            }
        }

        foreach (var (areaId, types) in areaSupplyTypes)
            areaCounts[areaId] = areaCounts.GetValueOrDefault(areaId) + types.Count;

        return new InventoryPlacement
        {
            AreaItemCounts = areaCounts,
            UnplacedItems = unplaced,
        };
    }

    /// <summary>Walks the container chain from <paramref name="startId"/> up; broken or cyclic chain ⇒ null.</summary>
    private static Guid? ResolveAreaViaContainers(
        Guid? startId,
        IReadOnlyDictionary<Guid, ContainerNode> containers,
        HashSet<Guid> visited)
    {
        var currentId = startId;
        while (currentId is { } id)
        {
            if (!visited.Add(id) || !containers.TryGetValue(id, out var node))
                return null;

            if (node.AreaId is not null)
                return node.AreaId;

            currentId = node.ParentItemId;
        }

        return null;
    }

    private readonly record struct ContainerNode(Guid? AreaId, Guid? ParentItemId);
}
