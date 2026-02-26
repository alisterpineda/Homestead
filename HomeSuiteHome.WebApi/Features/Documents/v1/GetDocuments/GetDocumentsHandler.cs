using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocuments;

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
                ContentType = d.ContentType,
                FileSize = d.FileSize,
                FileExtension = d.FileExtension,
                CreatedByUserId = d.CreatedByUserId,
                CreatedByUserName = d.CreatedByUser != null ? d.CreatedByUser.DisplayName : null,
                OwnerUserId = d.OwnerUserId,
                OwnerUserName = d.OwnerUser != null ? d.OwnerUser.DisplayName : null,
                Tags = d.DocumentTags.Select(dt => new GetDocumentsTagDto
                {
                    Id = dt.Tag.Id,
                    Name = dt.Tag.Name,
                    Path = dt.Tag.Path
                }).ToList(),
                Parties = d.DocumentParties.Select(dp => new GetDocumentsPartyDto
                {
                    Id = dp.Party.Id,
                    Name = dp.Party.Name
                }).ToList()
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
