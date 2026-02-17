using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.GetDocuments;

public class GetDocumentsHandler(AppDbContext db)
    : IQueryHandler<GetDocumentsRequest, PaginatedResponse<GetDocumentsResponse>>
{
    public async Task<PaginatedResponse<GetDocumentsResponse>> HandleAsync(
        GetDocumentsRequest request, CancellationToken ct)
    {
        var query = db.Documents.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(d => d.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(d => new GetDocumentsResponse
            {
                Id = d.Id,
                Name = d.Name,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSize = d.FileSize
            })
            .ToListAsync(ct);

        return new PaginatedResponse<GetDocumentsResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
