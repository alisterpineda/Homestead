using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentRevision;

public class GetDocumentRevisionHandler(AppDbContext db)
    : IQueryHandler<GetDocumentRevisionRequest, GetDocumentRevisionResponse?>
{
    public async Task<GetDocumentRevisionResponse?> HandleAsync(GetDocumentRevisionRequest request, CancellationToken ct)
    {
        return await db.DocumentRevisions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .Select(r => new GetDocumentRevisionResponse
            {
                Id = r.Id,
                RevisionNumber = r.RevisionNumber,
                Label = r.Label,
                Notes = r.Notes,
                ContentType = r.ContentType,
                FileSize = r.FileSize,
                FileHash = r.FileHash,
                FileExtension = r.FileExtension,
                ExtractedText = r.ExtractedText,
                CreatedAt = r.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
    }
}
