using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentVersion;

public class GetDocumentVersionHandler(AppDbContext db)
    : IQueryHandler<GetDocumentVersionRequest, GetDocumentVersionResponse?>
{
    public async Task<GetDocumentVersionResponse?> HandleAsync(GetDocumentVersionRequest request, CancellationToken ct)
    {
        return await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .Select(r => new GetDocumentVersionResponse
            {
                Id = r.Id,
                VersionNumber = r.VersionNumber,
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
