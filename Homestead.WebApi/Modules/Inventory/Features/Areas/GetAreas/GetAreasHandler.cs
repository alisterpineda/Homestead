using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;

public class GetAreasHandler(AppDbContext db)
    : IRequestHandler<GetAreasQuery, PaginatedResponse<AreaResponse>>
{
    public async Task<PaginatedResponse<AreaResponse>> Handle(
        GetAreasQuery request, CancellationToken ct)
    {
        var ordered = db.Areas.AsNoTracking().OrderBy(a => a.Name);

        var totalCount = await ordered.CountAsync(ct);
        var pageItems = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var pageIds = pageItems.Select(a => a.Id).ToHashSet();

        // Live = directly-placed (AreaId set), non-terminal items.
        var liveCounts = await db.Items
            .AsNoTracking()
            .Where(i => i.AreaId != null
                && pageIds.Contains(i.AreaId.Value)
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .GroupBy(i => i.AreaId!.Value)
            .Select(g => new { AreaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AreaId, x => x.Count, ct);

        var responses = pageItems
            .Select(a => AreaResponseMapper.ToResponse(a, liveCounts.GetValueOrDefault(a.Id)))
            .ToList();

        return new PaginatedResponse<AreaResponse>
        {
            Items = responses,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
        };
    }
}
