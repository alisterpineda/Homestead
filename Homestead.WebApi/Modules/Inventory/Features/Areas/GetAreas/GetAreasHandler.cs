using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;

public class GetAreasHandler(AppDbContext db, IInventoryPlacementService placementService)
    : IRequestHandler<GetAreasQuery, PaginatedResponse<AreaResponse>>
{
    public async Task<PaginatedResponse<AreaResponse>> Handle(
        GetAreasQuery request, CancellationToken ct)
    {
        // Name-ordered first so siblings stay name-ordered after the tree pass below (collation
        // parity with the SQL ORDER BY used elsewhere). Household scale — loading all is cheap.
        var areas = await db.Areas.AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        // Pre-order DFS: each parent immediately precedes its (name-sorted) descendants, so the
        // client can render the hierarchy in the order returned without a second sort.
        var ordered = TreeOrder(areas);

        var totalCount = ordered.Count;
        var pageItems = ordered
            .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
            .Take(request.EffectivePageSize)
            .ToList();

        var placement = await placementService.ComputeAsync(ct);
        var namesById = areas.ToDictionary(a => a.Id, a => a.Name);

        var responses = pageItems
            .Select(a => AreaResponseMapper.ToResponse(
                a,
                placement.AreaItemCounts.GetValueOrDefault(a.Id),
                a.ParentAreaId is { } pid ? namesById.GetValueOrDefault(pid) : null))
            .ToList();

        return new PaginatedResponse<AreaResponse>
        {
            Items = responses,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = totalCount,
        };
    }

    // Orders a name-sorted area list into a stable pre-order traversal (roots, each followed by its
    // descendants). Siblings keep the input (name) order. Cycle-guarded; any area unreachable from
    // a root (only possible via a manually-introduced cycle) is appended so nothing is hidden.
    private static List<Area> TreeOrder(IReadOnlyList<Area> areas)
    {
        var childrenByParent = new Dictionary<Guid, List<Area>>();
        foreach (var a in areas)
        {
            var key = a.ParentAreaId ?? Guid.Empty;
            if (!childrenByParent.TryGetValue(key, out var list))
                childrenByParent[key] = list = [];
            list.Add(a);
        }

        var ordered = new List<Area>(areas.Count);
        var emitted = new HashSet<Guid>();

        void Visit(Guid parentKey)
        {
            if (!childrenByParent.TryGetValue(parentKey, out var children))
                return;
            foreach (var child in children)
            {
                if (!emitted.Add(child.Id))
                    continue;
                ordered.Add(child);
                Visit(child.Id);
            }
        }

        Visit(Guid.Empty);

        foreach (var a in areas)
            if (emitted.Add(a.Id))
                ordered.Add(a);

        return ordered;
    }
}
