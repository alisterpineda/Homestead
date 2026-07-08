using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;

public class GetConsumablesHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetConsumablesQuery, PaginatedResponse<ConsumableResponse>>
{
    public async Task<PaginatedResponse<ConsumableResponse>> Handle(
        GetConsumablesQuery request, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.Fungible);

        // A consumable's only terminal status is Archived; the live filter hides it by default.
        if (request.IncludeInactive != true)
            query = query.Where(i => i.Status == ItemStatus.Active);

        if (request.LowStockOnly == true)
            query = query.Where(i =>
                i.RestockThreshold != null && i.StockEntries.Sum(e => e.Quantity) <= i.RestockThreshold);

        if (request.BrandId is { } brandId)
            query = query.Where(i => i.BrandId == brandId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(search));
        }

        var ordered = query.OrderBy(i => i.Name).Select(ConsumableResponseMapper.Projection);

        // Fast path (the common case): no resolved-area filter — SQL-paginate, then attach the
        // resolved per-location breakdown for just the page (same pattern as GetDurablesHandler).
        if (request.AreaId is null && request.Unplaced != true)
        {
            var page = await ordered.ToPaginatedAsync(request, ct);
            return page with
            {
                Items = await ConsumableStockLocationLoader.AttachStockLocationsAsync(db, locationResolver, page.Items, ct),
            };
        }

        // Resolved-area path: a consumable's stock→area resolution runs in C#, not SQL, so materialize
        // the full candidate set, attach stock locations (resolves each), filter by resolved area in
        // memory, then paginate in memory. Household scale, same as the placement service pays.
        var all = await ordered.ToListAsync(ct);
        var enriched = await ConsumableStockLocationLoader.AttachStockLocationsAsync(db, locationResolver, all, ct);

        IReadOnlyList<ConsumableResponse> filtered;
        if (request.AreaId is { } areaId)
        {
            var targetAreas = await AreaDescendants.InclusiveSubtreeAsync(db, areaId, ct);
            filtered = enriched
                .Where(c => c.StockLocations.Any(loc => ResolvedAreaOf(loc) is { } r && targetAreas.Contains(r)))
                .ToList();
        }
        else // request.Unplaced == true
        {
            filtered = enriched
                .Where(c => c.StockLocations.Any(loc => ResolvedAreaOf(loc) is null))
                .ToList();
        }

        return new PaginatedResponse<ConsumableResponse>
        {
            Items = filtered
                .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
                .Take(request.EffectivePageSize)
                .ToList(),
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = filtered.Count,
        };
    }

    // Resolved (leaf) area of a stock location: the LAST Area-kind segment of its path (the area chain
    // precedes any container segments). Mirrors the frontend's entryResolvedAreaId. Null when the
    // location resolves to no area — a broken/unplaced container chain.
    private static Guid? ResolvedAreaOf(StockEntryResponse location)
    {
        Guid? resolved = null;
        foreach (var segment in location.Path)
            if (segment.Kind == LocationSegmentKind.Area)
                resolved = segment.Id;
        return resolved;
    }
}
