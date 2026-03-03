using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersion;

public class GetDocumentVersionHandler(AppDbContext db, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : IQueryHandler<GetDocumentVersionRequest, GetDocumentVersionResponse?>
{
    public async Task<GetDocumentVersionResponse?> HandleAsync(GetDocumentVersionRequest request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Read);

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
                CreatedAt = r.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
    }
}
