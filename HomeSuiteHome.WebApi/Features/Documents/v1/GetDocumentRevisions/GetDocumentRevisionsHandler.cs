using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentRevisions;

public class GetDocumentRevisionsHandler(AppDbContext db)
    : IQueryHandler<GetDocumentRevisionsRequest, PaginatedResponse<GetDocumentRevisionsResponse>>
{
    public async Task<PaginatedResponse<GetDocumentRevisionsResponse>> HandleAsync(
        GetDocumentRevisionsRequest request, CancellationToken ct)
    {
        var query = db.DocumentRevisions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.DocumentId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.RevisionNumber)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new GetDocumentRevisionsResponse
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
            .ToListAsync(ct);

        return new PaginatedResponse<GetDocumentRevisionsResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
