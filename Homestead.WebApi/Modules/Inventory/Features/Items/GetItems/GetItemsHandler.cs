using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItems;

public class GetItemsHandler(AppDbContext db)
    : IRequestHandler<GetItemsQuery, PaginatedResponse<ItemResponse>>
{
    public async Task<PaginatedResponse<ItemResponse>> Handle(
        GetItemsQuery request, CancellationToken ct)
    {
        // v1 surfaces non-fungible items only.
        var query = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible);

        if (request.IncludeInactive != true)
            query = query.Where(i => ItemStatusExtensions.LiveStatuses.Contains(i.Status));

        if (request.AreaId is { } areaId)
            query = query.Where(i => i.AreaId == areaId);

        if (request.Unplaced == true)
            query = query.Where(i => i.AreaId == null && i.ParentItemId == null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(search));
        }

        return await query
            .OrderBy(i => i.Name)
            .Select(ItemResponseMapper.Projection)
            .ToPaginatedAsync(request, ct);
    }
}
