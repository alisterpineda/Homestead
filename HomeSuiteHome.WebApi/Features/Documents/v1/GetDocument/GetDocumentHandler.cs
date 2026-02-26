using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocument;

public class GetDocumentHandler(AppDbContext db)
    : IQueryHandler<GetDocumentRequest, GetDocumentResponse?>
{
    public async Task<GetDocumentResponse?> HandleAsync(GetDocumentRequest request, CancellationToken ct)
    {
        return await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => new GetDocumentResponse
            {
                Id = d.Id,
                Name = d.Name,
                ContentType = d.ContentType,
                FileSize = d.FileSize,
                FileHash = d.FileHash,
                FileExtension = d.FileExtension,
                CreatedAt = d.CreatedAt,
                CreatedByUserId = d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                OwnerUserId = d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                ExtractedText = d.ExtractedText,
                Tags = d.DocumentTags.Select(dt => new GetDocumentTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path
                }).ToList(),
                Parties = d.DocumentParties.Select(dp => new GetDocumentPartyDto
                {
                    Id = dp.Party.Id,
                    Name = dp.Party.Name,
                    Description = dp.Party.Description
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}
