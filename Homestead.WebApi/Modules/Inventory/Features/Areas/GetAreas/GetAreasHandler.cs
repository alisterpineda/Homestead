using Homestead.WebApi.Common;
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
        var ordered = db.Areas.AsNoTracking().OrderBy(a => a.Name);

        var totalCount = await ordered.CountAsync(ct);
        var pageItems = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var placement = await placementService.ComputeAsync(ct);

        var responses = pageItems
            .Select(a => AreaResponseMapper.ToResponse(
                a, placement.AreaItemCounts.GetValueOrDefault(a.Id)))
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
