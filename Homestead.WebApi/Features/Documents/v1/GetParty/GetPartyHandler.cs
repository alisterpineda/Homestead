using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetParty;

public class GetPartyHandler(AppDbContext db)
    : IQueryHandler<GetPartyRequest, GetPartyResponse?>
{
    public async Task<GetPartyResponse?> HandleAsync(GetPartyRequest request, CancellationToken ct)
    {
        return await db.Parties
            .AsNoTracking()
            .Where(p => p.Id == request.Id)
            .Select(p => new GetPartyResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CreatedAt = p.CreatedAt,
                Documents = p.DocumentParties.Select(dp => new GetPartyDocumentDto
                {
                    Id = dp.Document.Id,
                    Name = dp.Document.Name,
                    ContentType = dp.Document.Versions
                        .OrderByDescending(r => r.VersionNumber)
                        .Select(r => r.ContentType)
                        .FirstOrDefault() ?? ""
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}
