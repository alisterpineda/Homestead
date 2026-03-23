using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetParties;

public class GetPartiesHandler(AppDbContext db, IDocumentQueryService accessService)
    : IRequestHandler<GetPartiesQuery, PaginatedResponse<GetPartiesResponse>>
{
    public async Task<PaginatedResponse<GetPartiesResponse>> Handle(
        GetPartiesQuery request, CancellationToken ct)
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

        return await ApplySort(query, request.SortBy, request.SortDir, accessibleDocIds)
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

    private static IOrderedQueryable<Domains.Parties.Party> ApplySort(
        IQueryable<Domains.Parties.Party> query, PartySortBy? sortBy, SortDirection? sortDir,
        IQueryable<Guid>? accessibleDocIds)
    {
        var effectiveSortBy = sortBy ?? PartySortBy.Name;
        var effectiveSortDir = sortDir ?? SortDirection.Asc;

        return (effectiveSortBy, effectiveSortDir) switch
        {
            (PartySortBy.Name, SortDirection.Asc) => query.OrderBy(p => p.Name),
            (PartySortBy.Name, SortDirection.Desc) => query.OrderByDescending(p => p.Name),
            (PartySortBy.DocumentCount, SortDirection.Asc) => accessibleDocIds != null
                ? query.OrderBy(p => p.DocumentParties.Count(dp => accessibleDocIds.Contains(dp.DocumentId)))
                : query.OrderBy(p => p.DocumentParties.Count),
            (PartySortBy.DocumentCount, SortDirection.Desc) => accessibleDocIds != null
                ? query.OrderByDescending(p => p.DocumentParties.Count(dp => accessibleDocIds.Contains(dp.DocumentId)))
                : query.OrderByDescending(p => p.DocumentParties.Count),
            (PartySortBy.CreatedAt, SortDirection.Asc) => query.OrderBy(p => p.CreatedAt),
            (PartySortBy.CreatedAt, SortDirection.Desc) => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name),
        };
    }
}
