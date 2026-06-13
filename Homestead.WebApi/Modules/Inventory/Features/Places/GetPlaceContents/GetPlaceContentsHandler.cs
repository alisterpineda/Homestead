using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Modules.Inventory.Features.Supplies;
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
            .Select(ItemResponseMapper.Projection)
            .ToListAsync(ct);

        var childContainers = children.Where(c => c.IsContainer).ToList();
        var items = await locationResolver.EnrichAsync(
            children.Where(c => !c.IsContainer).ToList(), ct);

        var supplies = await LoadSuppliesAsync(request, ct);

        var placement = await placementService.ComputeAsync(ct);

        var containers = await BuildContainerSummariesAsync(childContainers, placement, ct);

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
        var directCount = containers.Count + items.Count + supplies.Count;

        return new PlaceContentsResponse
        {
            Path = path,
            ContainerItem = containerItem,
            DirectItemCount = directCount,
            NestedItemCount = Math.Max(0, total - directCount),
            Containers = containers,
            Items = items,
            Supplies = supplies,
        };
    }

    /// <summary>Validates the scoped place exists and resolves its breadcrumb path.</summary>
    private async Task<(IReadOnlyList<LocationSegmentResponse> Path, ItemResponse? ContainerItem)>
        ResolveScopeAsync(GetPlaceContentsQuery request, CancellationToken ct)
    {
        if (request.AreaId is { } areaId)
        {
            var area = await db.Areas.AsNoTracking()
                .Where(a => a.Id == areaId)
                .Select(a => new { a.Id, a.Name })
                .FirstOrDefaultAsync(ct)
                ?? throw new EntityNotFoundException("Area not found.");
            return ([new LocationSegmentResponse
            {
                Id = area.Id,
                Name = area.Name,
                Kind = LocationSegmentKind.Area,
            }], null);
        }

        if (request.ContainerItemId is { } containerId)
        {
            var container = await db.Items.AsNoTracking()
                .Where(i => i.Id == containerId && i.IsContainer)
                .Select(ItemResponseMapper.Projection)
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

    private async Task<List<PlaceSupplyResponse>> LoadSuppliesAsync(
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

        var supplyIds = localEntries.Select(e => e.ItemId).ToList();
        var supplyResponses = await db.Items.AsNoTracking()
            .Where(i => supplyIds.Contains(i.Id))
            .OrderBy(i => i.Name)
            .Select(SupplyResponseMapper.Projection)
            .ToListAsync(ct);

        // Full per-location breakdown, shared with GetSuppliesHandler, so the quick panel opened
        // from a place row behaves identically to one opened from the list.
        var enriched = await SupplyStockLocationLoader.AttachStockLocationsAsync(
            db, locationResolver, supplyResponses, ct);

        // Unique-per-(item, location) guarantees one local entry per supply.
        var localQuantities = localEntries.ToDictionary(e => e.ItemId, e => e.Quantity);

        return enriched
            .Select(s => new PlaceSupplyResponse
            {
                Supply = s,
                LocalQuantity = localQuantities[s.Id],
            })
            .ToList();
    }

    private async Task<List<PlaceContainerSummary>> BuildContainerSummariesAsync(
        IReadOnlyList<ItemResponse> childContainers,
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
        var supplyNames = await db.StockEntries.AsNoTracking()
            .Where(e => e.ContainerItemId != null && childIds.Contains(e.ContainerItemId.Value)
                && ItemStatusExtensions.LiveStatuses.Contains(e.Item.Status))
            .Select(e => new { ParentId = e.ContainerItemId!.Value, e.Item.Name })
            .ToListAsync(ct);
        var namesByContainer = childItemNames.Concat(supplyNames).ToLookup(n => n.ParentId, n => n.Name);

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
