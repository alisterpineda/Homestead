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
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.CreatedAt,
                d.DocumentDate,
                d.ValidFrom,
                d.ValidUntil,
                d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                LatestRevision = d.Revisions
                    .OrderByDescending(r => r.RevisionNumber)
                    .Select(r => new
                    {
                        r.ContentType,
                        r.FileSize,
                        r.FileHash,
                        r.FileExtension,
                        r.ExtractedText
                    })
                    .FirstOrDefault(),
                Revisions = d.Revisions
                    .OrderByDescending(r => r.RevisionNumber)
                    .Select(r => new GetDocumentRevisionDto
                    {
                        Id = r.Id,
                        RevisionNumber = r.RevisionNumber,
                        Label = r.Label,
                        Notes = r.Notes,
                        ContentType = r.ContentType,
                        FileSize = r.FileSize,
                        FileExtension = r.FileExtension,
                        CreatedAt = r.CreatedAt
                    })
                    .ToList(),
                RevisionCount = d.Revisions.Count,
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
            .Select(d => d.LatestRevision == null ? null : new GetDocumentResponse
            {
                Id = d.Id,
                Name = d.Name,
                ContentType = d.LatestRevision.ContentType,
                FileSize = d.LatestRevision.FileSize,
                FileHash = d.LatestRevision.FileHash,
                FileExtension = d.LatestRevision.FileExtension,
                CreatedAt = d.CreatedAt,
                DocumentDate = d.DocumentDate,
                ValidFrom = d.ValidFrom,
                ValidUntil = d.ValidUntil,
                CreatedByUserId = d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUserName,
                OwnerUserId = d.OwnerUserId,
                OwnerUserName = d.OwnerUserName,
                ExtractedText = d.LatestRevision.ExtractedText,
                RevisionCount = d.RevisionCount,
                Revisions = d.Revisions,
                Tags = d.Tags,
                Parties = d.Parties
            })
            .FirstOrDefaultAsync(ct);
    }
}
