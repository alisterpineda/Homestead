using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Tags.GetTags;

public class GetTagsHandler(AppDbContext db)
    : IRequestHandler<GetTagsQuery, PaginatedResponse<GetTagsResponse>>
{
    public async Task<PaginatedResponse<GetTagsResponse>> Handle(GetTagsQuery request, CancellationToken ct)
    {
        var query = db.Tags.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{request.Search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, searchPattern) ||
                EF.Functions.ILike(t.Path, searchPattern));
        }
        else if (!string.IsNullOrWhiteSpace(request.ParentPath))
        {
            var parentPath = request.ParentPath.Trim().Trim('/');

            if (request.IncludeChildren == true)
            {
                query = query.Where(t => t.Path.StartsWith(parentPath + "/"));
            }
            else
            {
                // Direct children only: path starts with parentPath/ and has no further /
                var parent = await db.Tags.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Path == parentPath, ct);

                query = parent is not null
                    ? query.Where(t => t.ParentId == parent.Id)
                    : query.Where(t => false);
            }
        }
        // No params: return all tags

        return await ApplySort(query, request.SortBy, request.SortDir)
            .Select(t => new GetTagsResponse
            {
                Id = t.Id,
                Name = t.Name,
                Path = t.Path,
                ParentId = t.ParentId,
                Color = t.Color,
                EffectiveColor = t.EffectiveColor,
                Description = t.Description,
                CreatedAt = t.CreatedAt,
                DocumentCount = t.DocumentTags.Count
            })
            .ToPaginatedAsync(request, ct);
    }

    private static IOrderedQueryable<Domain.Documents.Tag> ApplySort(
        IQueryable<Domain.Documents.Tag> query, TagSortBy? sortBy, SortDirection? sortDir)
    {
        var effectiveSortBy = sortBy ?? TagSortBy.Name;
        var effectiveSortDir = sortDir ?? SortDirection.Asc;

        return (effectiveSortBy, effectiveSortDir) switch
        {
            (TagSortBy.Name, SortDirection.Asc) => query.OrderBy(t => t.Name),
            (TagSortBy.Name, SortDirection.Desc) => query.OrderByDescending(t => t.Name),
            (TagSortBy.Path, SortDirection.Asc) => query.OrderBy(t => t.Path),
            (TagSortBy.Path, SortDirection.Desc) => query.OrderByDescending(t => t.Path),
            (TagSortBy.DocumentCount, SortDirection.Asc) => query.OrderBy(t => t.DocumentTags.Count),
            (TagSortBy.DocumentCount, SortDirection.Desc) => query.OrderByDescending(t => t.DocumentTags.Count),
            (TagSortBy.CreatedAt, SortDirection.Asc) => query.OrderBy(t => t.CreatedAt),
            (TagSortBy.CreatedAt, SortDirection.Desc) => query.OrderByDescending(t => t.CreatedAt),
            _ => query.OrderBy(t => t.Name),
        };
    }
}
