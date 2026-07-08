using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Search;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTags;

public class GetTagsHandler(AppDbContext db)
    : IRequestHandler<GetInventoryTagsQuery, PaginatedResponse<GetInventoryTagsResponse>>
{
    public async Task<PaginatedResponse<GetInventoryTagsResponse>> Handle(GetInventoryTagsQuery request, CancellationToken ct)
    {
        var query = db.InventoryTags.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchPattern = $"%{LikeEscaper.Escape(request.Search.Trim())}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, searchPattern, LikeEscaper.EscapeChar) ||
                EF.Functions.ILike(t.Path, searchPattern, LikeEscaper.EscapeChar));
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
                var parent = await db.InventoryTags.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Path == parentPath, ct);

                query = parent is not null
                    ? query.Where(t => t.ParentId == parent.Id)
                    : query.Where(t => false);
            }
        }
        // No params: return all tags

        return await ApplySort(query, request.SortBy, request.SortDir)
            .Select(t => new GetInventoryTagsResponse
            {
                Id = t.Id,
                Name = t.Name,
                Path = t.Path,
                ParentId = t.ParentId,
                Color = t.Color,
                EffectiveColor = t.EffectiveColor,
                Description = t.Description,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                UpdatedByUserId = t.UpdatedByUserId,
                UpdatedByUserName = t.UpdatedByUser != null ? t.UpdatedByUser.DisplayName : null,
                ItemCount = t.ItemTags.Count
            })
            .ToPaginatedAsync(request, ct);
    }

    private static IOrderedQueryable<Domain.Inventory.Tag> ApplySort(
        IQueryable<Domain.Inventory.Tag> query, InventoryTagSortBy? sortBy, SortDirection? sortDir)
    {
        var effectiveSortBy = sortBy ?? InventoryTagSortBy.Name;
        var effectiveSortDir = sortDir ?? SortDirection.Asc;

        return (effectiveSortBy, effectiveSortDir) switch
        {
            (InventoryTagSortBy.Name, SortDirection.Asc) => query.OrderBy(t => t.Name),
            (InventoryTagSortBy.Name, SortDirection.Desc) => query.OrderByDescending(t => t.Name),
            (InventoryTagSortBy.Path, SortDirection.Asc) => query.OrderBy(t => t.Path),
            (InventoryTagSortBy.Path, SortDirection.Desc) => query.OrderByDescending(t => t.Path),
            (InventoryTagSortBy.ItemCount, SortDirection.Asc) => query.OrderBy(t => t.ItemTags.Count),
            (InventoryTagSortBy.ItemCount, SortDirection.Desc) => query.OrderByDescending(t => t.ItemTags.Count),
            (InventoryTagSortBy.CreatedAt, SortDirection.Asc) => query.OrderBy(t => t.CreatedAt),
            (InventoryTagSortBy.CreatedAt, SortDirection.Desc) => query.OrderByDescending(t => t.CreatedAt),
            (InventoryTagSortBy.UpdatedAt, SortDirection.Asc) => query.OrderBy(t => t.UpdatedAt),
            (InventoryTagSortBy.UpdatedAt, SortDirection.Desc) => query.OrderByDescending(t => t.UpdatedAt),
            _ => query.OrderBy(t => t.Name),
        };
    }
}
