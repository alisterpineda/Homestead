using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Documents.GetDocumentVersion;

public class GetDocumentVersionHandler(AppDbContext db)
    : IRequestHandler<GetDocumentVersionQuery, GetDocumentVersionResponse?>
{
    public async Task<GetDocumentVersionResponse?> Handle(GetDocumentVersionQuery request, CancellationToken ct)
    {
        return await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .Select(r => new GetDocumentVersionResponse
            {
                Id = r.Id,
                VersionNumber = r.VersionNumber,
                Label = r.Label,
                ContentType = r.ContentType,
                FileSize = r.FileSize,
                FileHash = r.FileHash,
                FileExtension = r.FileExtension,
                ExtractedText = r.ExtractedText,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);
    }
}
