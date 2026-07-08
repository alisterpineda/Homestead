using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

public class GetTagsByIdsHandler(AppDbContext db)
    : IRequestHandler<GetInventoryTagsByIdsQuery, IReadOnlyList<GetInventoryTagsByIdsResponse>>
{
    public async Task<IReadOnlyList<GetInventoryTagsByIdsResponse>> Handle(
        GetInventoryTagsByIdsQuery request, CancellationToken ct)
    {
        return await db.InventoryTags.AsNoTracking()
            .Where(t => request.Ids.Contains(t.Id))
            .Select(t => new GetInventoryTagsByIdsResponse
            {
                Id = t.Id,
                Path = t.Path,
            })
            .ToListAsync(ct);
    }
}
