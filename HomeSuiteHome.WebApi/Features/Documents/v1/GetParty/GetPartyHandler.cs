using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetParty;

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
                    ContentType = dp.Document.Revisions
                        .OrderByDescending(r => r.RevisionNumber)
                        .Select(r => r.ContentType)
                        .FirstOrDefault() ?? ""
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}
