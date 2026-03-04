using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetParty;

public class GetPartyHandler(AppDbContext db)
    : IQueryHandler<GetPartyRequest, GetPartyResponse?>
{
    public async Task<GetPartyResponse?> HandleAsync(GetPartyRequest request, CancellationToken ct)
    {
        var query = db.Parties.AsNoTracking().Where(p => p.Id == request.Id);

        return await query
            .Select(p => new GetPartyResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CreatedAt = p.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);
    }
}
