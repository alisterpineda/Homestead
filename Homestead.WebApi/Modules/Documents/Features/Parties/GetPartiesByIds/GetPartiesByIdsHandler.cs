using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public class GetPartiesByIdsHandler(AppDbContext db)
    : IRequestHandler<GetPartiesByIdsQuery, IReadOnlyList<GetPartiesByIdsResponse>>
{
    public async Task<IReadOnlyList<GetPartiesByIdsResponse>> Handle(
        GetPartiesByIdsQuery request, CancellationToken ct)
    {
        return await db.Parties.AsNoTracking()
            .Where(p => request.Ids.Contains(p.Id))
            .Select(p => new GetPartiesByIdsResponse
            {
                Id = p.Id,
                Name = p.Name,
            })
            .ToListAsync(ct);
    }
}
