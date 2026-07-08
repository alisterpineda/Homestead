using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurables;

public class GetDurablesHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetDurablesQuery, PaginatedResponse<DurableResponse>>
{
    public async Task<PaginatedResponse<DurableResponse>> Handle(
        GetDurablesQuery request, CancellationToken ct)
    {
        // v1 surfaces non-fungible durables only.
        var query = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible);

        if (request.IncludeInactive != true)
            query = query.Where(i => ItemStatusExtensions.LiveStatuses.Contains(i.Status));

        if (request.ParentItemId is { } parentItemId)
            query = query.Where(i => i.ParentItemId == parentItemId);

        if (request.BrandId is { } brandId)
            query = query.Where(i => i.BrandId == brandId);

        if (request.DurableKinds is { Length: > 0 } kinds)
            query = query.Where(i => i.DurableKind != null && kinds.Contains(i.DurableKind.Value));

        if (request.TagIds is { Length: > 0 })
        {
            var tagIds = await InventoryTagFilter.ExpandTagIdsAsync(db, request.TagIds, request.IncludeChildTags == true, ct);
            query = query.Where(i => i.ItemTags.Any(it => tagIds.Contains(it.TagId)));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(search));
        }

        var ordered = query.OrderBy(i => i.Name).Select(DurableResponseMapper.Projection);

        // Fast path (the common case): no resolved-area filter — SQL-paginate, enrich just the page.
        if (request.AreaId is null && request.Unplaced != true)
        {
            var page = await ordered.ToPaginatedAsync(request, ct);
            return page with { Items = await locationResolver.EnrichAsync(page.Items, ct) };
        }

        // Resolved-area path: resolved area is neither stored nor SQL-expressible (the container-chain
        // walk runs in C#), so materialize the full filtered candidate set, enrich it (populates
        // ResolvedAreaId), filter by resolved area in memory, then paginate in memory. Household scale
        // — the same cost InventoryPlacementService.ComputeAsync already pays.
        var all = await ordered.ToListAsync(ct);
        var enriched = await locationResolver.EnrichAsync(all, ct);

        IReadOnlyList<DurableResponse> filtered;
        if (request.AreaId is { } areaId)
        {
            var targetAreas = await AreaDescendants.InclusiveSubtreeAsync(db, areaId, ct);
            filtered = enriched.Where(i => i.ResolvedAreaId is { } r && targetAreas.Contains(r)).ToList();
        }
        else // request.Unplaced == true
        {
            filtered = enriched.Where(i => i.ResolvedAreaId is null).ToList();
        }

        return new PaginatedResponse<DurableResponse>
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
}
