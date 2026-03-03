using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetParty;

public class GetPartyHandler(AppDbContext db, IDocumentQueryService accessService)
    : IQueryHandler<GetPartyRequest, GetPartyResponse?>
{
    public async Task<GetPartyResponse?> HandleAsync(GetPartyRequest request, CancellationToken ct)
    {
        var query = db.Parties.AsNoTracking().Where(p => p.Id == request.Id);

        if (!accessService.IsAdmin)
        {
            var accessibleDocIds = accessService.AccessibleDocumentIds();
            query = query.Where(p =>
                p.DocumentParties.Any(dp => accessibleDocIds.Contains(dp.DocumentId)));
        }

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
