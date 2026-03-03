using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;

public class GetDocumentVersionsHandler(AppDbContext db, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : IQueryHandler<GetDocumentVersionsRequest, PaginatedResponse<GetDocumentVersionsResponse>>
{
    public async Task<PaginatedResponse<GetDocumentVersionsResponse>> HandleAsync(
        GetDocumentVersionsRequest request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Read);

        var query = db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.VersionNumber)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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
            .ToListAsync(ct);

        return new PaginatedResponse<GetDocumentVersionsResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
