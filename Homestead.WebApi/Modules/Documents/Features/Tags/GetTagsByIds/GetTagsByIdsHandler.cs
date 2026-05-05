using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public class GetTagsByIdsHandler(AppDbContext db)
    : IRequestHandler<GetTagsByIdsQuery, IReadOnlyList<GetTagsByIdsResponse>>
{
    public async Task<IReadOnlyList<GetTagsByIdsResponse>> Handle(
        GetTagsByIdsQuery request, CancellationToken ct)
    {
        return await db.Tags.AsNoTracking()
            .Where(t => request.Ids.Contains(t.Id))
            .Select(t => new GetTagsByIdsResponse
            {
                Id = t.Id,
                Path = t.Path,
            })
            .ToListAsync(ct);
    }
}
