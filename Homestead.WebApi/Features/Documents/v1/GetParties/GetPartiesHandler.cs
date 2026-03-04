using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetParties;

public class GetPartiesHandler(AppDbContext db, IDocumentQueryService accessService)
    : IQueryHandler<GetPartiesRequest, PaginatedResponse<GetPartiesResponse>>
{
    public async Task<PaginatedResponse<GetPartiesResponse>> HandleAsync(
        GetPartiesRequest request, CancellationToken ct)
    {
        var query = db.Parties.AsNoTracking();

        // Compute accessible doc IDs for scoped DocumentCount
        var accessibleDocIds = !accessService.IsAdmin
            ? accessService.AccessibleDocumentIds()
            : null;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(search) ||
                (p.Description != null && p.Description.ToLower().Contains(search)));
        }

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new GetPartiesResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CreatedAt = p.CreatedAt,
                DocumentCount = accessibleDocIds != null
                    ? p.DocumentParties.Count(dp => accessibleDocIds.Contains(dp.DocumentId))
                    : p.DocumentParties.Count
            })
            .ToPaginatedAsync(request, ct);
    }
}
