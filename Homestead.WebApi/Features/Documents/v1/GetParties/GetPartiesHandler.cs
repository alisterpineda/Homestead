using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetParties;

public class GetPartiesHandler(AppDbContext db)
    : IQueryHandler<GetPartiesRequest, PaginatedResponse<GetPartiesResponse>>
{
    public async Task<PaginatedResponse<GetPartiesResponse>> HandleAsync(
        GetPartiesRequest request, CancellationToken ct)
    {
        var query = db.Parties.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(search) ||
                (p.Description != null && p.Description.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new GetPartiesResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CreatedAt = p.CreatedAt,
                DocumentCount = p.DocumentParties.Count
            })
            .ToListAsync(ct);

        return new PaginatedResponse<GetPartiesResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
