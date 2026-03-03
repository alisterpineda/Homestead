using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetTags;

public class GetTagsHandler(AppDbContext db, IDocumentAccessService accessService)
    : IQueryHandler<GetTagsRequest, IReadOnlyList<GetTagsResponse>>
{
    public async Task<IReadOnlyList<GetTagsResponse>> HandleAsync(GetTagsRequest request, CancellationToken ct)
    {
        var query = db.Tags.AsNoTracking();

        // Visibility filter for non-Admin users
        if (!accessService.IsAdmin)
        {
            var accessibleDocIds = accessService.AccessibleDocumentIds();

            // Single query: get (TagId, Path) for all tags linked to accessible documents
            var visibleTags = await db.DocumentTags
                .Where(dt => accessibleDocIds.Contains(dt.DocumentId))
                .Select(dt => new { dt.TagId, dt.Tag.Path })
                .Distinct()
                .ToListAsync(ct);

            var visibleTagIds = visibleTags.Select(t => t.TagId).ToList();

            // Compute ancestor paths so the hierarchical tree is navigable
            var ancestorPaths = new HashSet<string>();
            foreach (var tag in visibleTags)
            {
                var segments = tag.Path.Split('/');
                var current = "";
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    current = i == 0 ? segments[0] : current + "/" + segments[i];
                    ancestorPaths.Add(current);
                }
            }

            var ancestorPathList = ancestorPaths.ToList();
            query = query.Where(t => visibleTagIds.Contains(t.Id) || ancestorPathList.Contains(t.Path));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLowerInvariant();
            query = query.Where(t => t.Name.Contains(search) || t.Path.Contains(search));
        }
        else if (!string.IsNullOrWhiteSpace(request.ParentPath))
        {
            var parentPath = request.ParentPath.Trim().ToLowerInvariant().Trim('/');

            if (request.IncludeChildren)
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

        return await query
            .OrderBy(t => t.Path)
            .Select(t => new GetTagsResponse
            {
                Id = t.Id,
                Name = t.Name,
                Path = t.Path,
                ParentId = t.ParentId,
                Color = t.Color
            })
            .ToListAsync(ct);
    }
}
