using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;

public class GetPlaceContentsHandler(
    AppDbContext db,
    IInventoryPlacementService placementService,
    IItemLocationResolver locationResolver)
    : IRequestHandler<GetPlaceContentsQuery, PlaceContentsResponse>
{
    private const int PreviewNameCount = 3;

    public async Task<PlaceContentsResponse> Handle(GetPlaceContentsQuery request, CancellationToken ct)
    {
        var scopeCount = (request.AreaId is not null ? 1 : 0)
            + (request.ContainerItemId is not null ? 1 : 0)
            + (request.Unplaced == true ? 1 : 0);
        if (scopeCount != 1)
            throw new BusinessRuleException("Specify exactly one of areaId, containerItemId, or unplaced.");

        var (path, containerItem) = await ResolveScopeAsync(request, ct);

        // Direct live children, name-ordered; containers become drillable cards, the rest the
        // inline list. Terminal containers themselves are allowed as the scope (deep links to
        // archived containers still render) — contents are live-filtered regardless.
        var childQuery = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status));
        childQuery = request switch
        {
            { AreaId: { } areaId } => childQuery.Where(i => i.AreaId == areaId),
            { ContainerItemId: { } containerId } => childQuery.Where(i => i.ParentItemId == containerId),
            _ => childQuery.Where(i => i.AreaId == null && i.ParentItemId == null),
        };
        var children = await childQuery
            .OrderBy(i => i.Name)
            .Select(DurableResponseMapper.Projection)
            .ToListAsync(ct);

        var childContainers = children.Where(c => c.IsContainer).ToList();
        var items = await locationResolver.EnrichAsync(
            children.Where(c => !c.IsContainer).ToList(), ct);

        var consumables = await LoadConsumablesAsync(request, ct);

        var placement = await placementService.ComputeAsync(ct);

        var containers = await BuildContainerSummariesAsync(childContainers, placement, ct);

        // Direct sub-areas (area scope only) render as drill cards beside the containers, each
        // carrying its own rolled subtree count.
        var childAreas = await LoadChildAreasAsync(request, placement, ct);

        // The scope's resolved total uses the exact semantics that drive the overview cards, so
        // direct + nested always reconciles with the count the user clicked.
        var total = request switch
        {
            { AreaId: { } areaId } => placement.AreaItemCounts.GetValueOrDefault(areaId),
            { ContainerItemId: { } containerId } =>
                placement.ContainerStats.TryGetValue(containerId, out var stats)
                    ? stats.ResolvedContentsCount
                    : 0,
            _ => placement.UnplacedItems.Count,
        };

        // Child-area cards count as direct cards (like containers): each is one drillable thing shown
        // here, carrying its own rolled subtree count. The "+N nested" badge means only things deeper
        // inside THIS place's OWN containers, so it's derived from the place's own (non-rolled) count
        // minus the direct leaf cards. For an area that's AreaOwnItemCounts (descendant areas
        // excluded, set-deduped — NOT a sum of rolled child counts, which would double-subtract any
        // consumable type shared between this area and a sub-area); for a container/Unplaced scope there
        // are no child areas, so the own count is just the rolled total.
        var ownCount = request.AreaId is { } scopeAreaId
            ? placement.AreaOwnItemCounts.GetValueOrDefault(scopeAreaId)
            : total;
        var directCount = containers.Count + items.Count + consumables.Count + childAreas.Count;
        var nestedItemCount = Math.Max(0, ownCount - containers.Count - items.Count - consumables.Count);

        // Scoped warning counts for the Browse drilled panels, resolved with the same semantics as
        // `total` above: low-stock consumables stored here + lent items from here. Count-only — the
        // actual items already appear in the contents list. Unplaced has neither (it's the absence
        // of a place: holds no stock, and a lent item without a resolved area is just unplaced).
        var scopedLowStockCount = request switch
        {
            { AreaId: { } areaId } => placement.AreaLowStockCounts.GetValueOrDefault(areaId),
            { ContainerItemId: { } containerId } =>
                placement.ContainerStats.TryGetValue(containerId, out var lowStats)
                    ? lowStats.LowStockCount
                    : 0,
            _ => 0,
        };
        var scopedLentCount = request switch
        {
            { AreaId: { } areaId } => placement.AreaLentCounts.GetValueOrDefault(areaId),
            { ContainerItemId: { } containerId } =>
                placement.ContainerStats.TryGetValue(containerId, out var lentStats)
                    ? lentStats.LentCount
                    : 0,
            _ => 0,
        };

        return new PlaceContentsResponse
        {
            Path = path,
            ContainerItem = containerItem,
            DirectItemCount = directCount,
            NestedItemCount = nestedItemCount,
            ScopedLowStockCount = scopedLowStockCount,
            ScopedLentCount = scopedLentCount,
            ChildAreas = childAreas,
            Containers = containers,
            Items = items,
            Consumables = consumables,
        };
    }

    private async Task<List<PlaceChildAreaSummary>> LoadChildAreasAsync(
        GetPlaceContentsQuery request, InventoryPlacement placement, CancellationToken ct)
    {
        if (request.AreaId is not { } areaId)
            return [];

        var children = await db.Areas.AsNoTracking()
            .Where(a => a.ParentAreaId == areaId)
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);

        return children
            .Select(a => new PlaceChildAreaSummary
            {
                Id = a.Id,
                Name = a.Name,
                ItemCount = placement.AreaItemCounts.GetValueOrDefault(a.Id),
                LowStockCount = placement.AreaLowStockCounts.GetValueOrDefault(a.Id),
            })
            .ToList();
    }

    /// <summary>Validates the scoped place exists and resolves its breadcrumb path.</summary>
    private async Task<(IReadOnlyList<LocationSegmentResponse> Path, DurableResponse? ContainerItem)>
        ResolveScopeAsync(GetPlaceContentsQuery request, CancellationToken ct)
    {
        if (request.AreaId is { } areaId)
        {
            var areaExists = await db.Areas.AsNoTracking().AnyAsync(a => a.Id == areaId, ct);
            if (!areaExists)
                throw new EntityNotFoundException("Area not found.");

            // Full ancestor breadcrumb (House › Basement › Bedroom), not just the leaf segment.
            var areaPath = await locationResolver.ResolveAreaPathAsync(areaId, ct);
            return (areaPath.Segments, null);
        }

        if (request.ContainerItemId is { } containerId)
        {
            var container = await db.Items.AsNoTracking()
                .Where(i => i.Id == containerId && i.IsContainer)
                .Select(DurableResponseMapper.Projection)
                .FirstOrDefaultAsync(ct)
                ?? throw new EntityNotFoundException("Container not found.");

            // The holding-location walk includes the holder as the last segment — exactly the
            // breadcrumb (area › ancestors › this container).
            var holding = new HoldingLocation(null, containerId);
            var paths = await locationResolver.ResolveHoldingLocationsAsync([holding], ct);
            return (
                paths.GetValueOrDefault(holding, LocationPath.Empty).Segments,
                await locationResolver.EnrichAsync(container, ct));
        }

        return ([], null);
    }

    private async Task<List<PlaceConsumableResponse>> LoadConsumablesAsync(
        GetPlaceContentsQuery request, CancellationToken ct)
    {
        // Unplaced holds no stock by construction (an entry always has a location).
        if (request.Unplaced == true)
            return [];

        var entriesQuery = db.StockEntries.AsNoTracking()
            .Where(e => ItemStatusExtensions.LiveStatuses.Contains(e.Item.Status));
        entriesQuery = request.AreaId is { } areaId
            ? entriesQuery.Where(e => e.AreaId == areaId)
            : entriesQuery.Where(e => e.ContainerItemId == request.ContainerItemId);
        var localEntries = await entriesQuery
            .Select(e => new { e.ItemId, e.Quantity })
            .ToListAsync(ct);
        if (localEntries.Count == 0)
            return [];

        var consumableIds = localEntries.Select(e => e.ItemId).ToList();
        var consumableResponses = await db.Items.AsNoTracking()
            .Where(i => consumableIds.Contains(i.Id))
            .OrderBy(i => i.Name)
            .Select(ConsumableResponseMapper.Projection)
            .ToListAsync(ct);

        // Full per-location breakdown, shared with GetConsumablesHandler, so the quick panel opened
        // from a place row behaves identically to one opened from the list.
        var enriched = await ConsumableStockLocationLoader.AttachStockLocationsAsync(
            db, locationResolver, consumableResponses, ct);

        // Unique-per-(item, location) guarantees one local entry per consumable.
        var localQuantities = localEntries.ToDictionary(e => e.ItemId, e => e.Quantity);

        return enriched
            .Select(s => new PlaceConsumableResponse
            {
                Consumable = s,
                LocalQuantity = localQuantities[s.Id],
            })
            .ToList();
    }

    private async Task<List<PlaceContainerSummary>> BuildContainerSummariesAsync(
        IReadOnlyList<DurableResponse> childContainers,
        InventoryPlacement placement,
        CancellationToken ct)
    {
        if (childContainers.Count == 0)
            return [];

        var childIds = childContainers.Select(c => c.Id).ToList();
        var childItemNames = await db.Items.AsNoTracking()
            .Where(i => i.ParentItemId != null && childIds.Contains(i.ParentItemId.Value)
                && i.Fungibility == Fungibility.NonFungible
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .Select(i => new { ParentId = i.ParentItemId!.Value, i.Name })
            .ToListAsync(ct);
        var consumableNames = await db.StockEntries.AsNoTracking()
            .Where(e => e.ContainerItemId != null && childIds.Contains(e.ContainerItemId.Value)
                && ItemStatusExtensions.LiveStatuses.Contains(e.Item.Status))
            .Select(e => new { ParentId = e.ContainerItemId!.Value, e.Item.Name })
            .ToListAsync(ct);
        var namesByContainer = childItemNames.Concat(consumableNames).ToLookup(n => n.ParentId, n => n.Name);

        return childContainers
            .Select(c =>
            {
                // One lookup, read by both counts — an empty subtree is absent ⇒ default (0, 0).
                placement.ContainerStats.TryGetValue(c.Id, out var stats);
                return new PlaceContainerSummary
                {
                    Id = c.Id,
                    Name = c.Name,
                    Status = c.Status,
                    ItemCount = stats.ResolvedContentsCount,
                    LowStockCount = stats.LowStockCount,
                    PreviewNames = namesByContainer[c.Id]
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Take(PreviewNameCount)
                        .ToList(),
                };
            })
            .ToList();
    }
}
