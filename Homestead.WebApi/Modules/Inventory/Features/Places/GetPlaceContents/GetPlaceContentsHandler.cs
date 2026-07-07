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

        // An Assembly scope lists its components: every direct child is a component regardless of its
        // own kind, so none are split off as drillable place-cards (an assembly is not a place, and a
        // component that is itself a container/assembly must still render in the parent's list).
        var scopeIsAssembly = containerItem?.DurableKind == DurableKind.Assembly;

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

        // Only Containers become drillable place cards, and only when the scope itself is a place. An
        // Assembly is not a place, so it falls into the plain-items list; and under an Assembly scope
        // ALL children are components (a Container child included), so nothing is split into cards.
        var childContainers = scopeIsAssembly
            ? new List<DurableResponse>()
            : children.Where(c => c.DurableKind == DurableKind.Container).ToList();
        var items = await locationResolver.EnrichAsync(
            (scopeIsAssembly
                ? children
                : children.Where(c => c.DurableKind != DurableKind.Container)).ToList(), ct);

        var consumables = await LoadConsumablesAsync(request, ct);

        var placement = await placementService.ComputeAsync(ct);

        var containers = await BuildContainerSummariesAsync(childContainers, placement, ct);

        // Direct sub-areas (area scope only) render as drill cards beside the containers, each
        // carrying its own rolled subtree count.
        var childAreas = await LoadChildAreasAsync(request, placement, ct);

        // Scoped warning counts for the Browse drilled panels. An AREA scope reports overview-card
        // (rolled-subtree) semantics, linkable via the ?area= items filter. A CONTAINER scope reports
        // its DIRECT contents instead — the items list can't scope to a container (Browse owns
        // containment), so the panel is count-only and the matching rows already appear in the contents
        // list. This computation used to live in the frontend; it moves here so the one place that
        // knows a container's direct contents also counts them (fixing the client miss of a Lent
        // sub-container). Unplaced has neither warning (it's the absence of a place).
        var scopedLowStockCount = request switch
        {
            { AreaId: { } areaId } => placement.AreaLowStockCounts.GetValueOrDefault(areaId),
            // Direct child consumables that are globally low (one row per consumable ⇒ already distinct).
            { ContainerItemId: not null } => consumables.Count(s => s.Consumable.IsLow),
            _ => 0,
        };
        var scopedLentCount = request switch
        {
            { AreaId: { } areaId } => placement.AreaLentCounts.GetValueOrDefault(areaId),
            // Direct lent children — `children` spans both non-container durables AND child containers,
            // so a child container that is itself Lent is counted (the client-side version missed it).
            { ContainerItemId: not null } => children.Count(c => c.Status == ItemStatus.Lent),
            _ => 0,
        };

        return new PlaceContentsResponse
        {
            Path = path,
            ContainerItem = containerItem,
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
            // Any holder is a valid scope: a Container (its stored-in contents) or an Assembly (its
            // components — reused by the detail page's Components section).
            var container = await db.Items.AsNoTracking()
                .Where(i => i.Id == containerId
                    && (i.DurableKind == DurableKind.Container || i.DurableKind == DurableKind.Assembly))
                .Select(DurableResponseMapper.Projection)
                .FirstOrDefaultAsync(ct)
                ?? throw new EntityNotFoundException("Container not found.");

            // The holding-location walk includes the holder as the last segment — exactly the
            // breadcrumb (area › ancestors › this holder).
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
