using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;

public class GetDocumentVersionsHandler(AppDbContext db, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : IRequestHandler<GetDocumentVersionsRequest, PaginatedResponse<GetDocumentVersionsResponse>>
{
    public async Task<PaginatedResponse<GetDocumentVersionsResponse>> Handle(
        GetDocumentVersionsRequest request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Read);

        return await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId)
            .OrderByDescending(r => r.VersionNumber)
            .Select(r => new GetDocumentVersionsResponse
            {
                Id = r.Id,
                VersionNumber = r.VersionNumber,
                Label = r.Label,
                ContentType = r.ContentType,
                FileSize = r.FileSize,
                FileExtension = r.FileExtension,
                CreatedAt = r.CreatedAt
            })
            .ToPaginatedAsync(request, ct);
    }
}
