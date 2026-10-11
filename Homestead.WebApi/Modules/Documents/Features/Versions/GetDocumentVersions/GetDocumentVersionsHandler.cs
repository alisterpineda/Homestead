using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;

public class GetDocumentVersionsHandler(AppDbContext db)
{
    public async Task<PaginatedResponse<GetDocumentVersionsResponse>> HandleAsync(
        Guid documentId, PaginatedRequest pagination, CancellationToken ct)
    {
        return await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.VersionNumber)
            .Select(r => new GetDocumentVersionsResponse
            {
                Id = r.Id,
                VersionNumber = r.VersionNumber,
                Label = r.Label,
                ContentType = r.ContentType,
                FileSize = r.FileSize,
                FileExtension = r.FileExtension,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToPaginatedAsync(pagination, ct);
    }
}
