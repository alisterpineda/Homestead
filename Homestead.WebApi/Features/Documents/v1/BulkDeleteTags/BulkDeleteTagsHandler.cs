using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

public class BulkDeleteTagsHandler(AppDbContext db, IDocumentQueryService accessService)
    : ICommandHandler<BulkDeleteTagsRequest, BulkDeleteTagsResponse>
{
    public async Task<BulkDeleteTagsResponse> HandleAsync(BulkDeleteTagsRequest request, CancellationToken ct)
    {
        var tags = await db.Tags
            .Where(t => request.Ids.Contains(t.Id))
            .ToListAsync(ct);

        if (tags.Count == 0)
            return new BulkDeleteTagsResponse(0);

        // Block deletion if any requested tags are used on inaccessible documents
        var relatedDocIds = db.DocumentTags
            .Where(dt => request.Ids.Contains(dt.TagId))
            .Select(dt => dt.DocumentId);

        await accessService.EnsureAllDocumentsWritableAsync(relatedDocIds, ct);

        var deleteIds = new HashSet<Guid>(tags.Select(t => t.Id));

        // Single query: batch-load all descendants of all deleted tags
        // Use Union per path to ensure clean EF Core SQL translation (same
        // pattern as GetDocumentsHandler's tag expansion).
        IQueryable<Domains.Documents.Tag>? descendantQuery = null;
        foreach (var path in tags.Select(t => t.Path))
        {
            var prefix = path + "/";
            var match = db.Tags.Where(t => t.Path.StartsWith(prefix));
            descendantQuery = descendantQuery is null ? match : descendantQuery.Union(match);
        }

        var allDescendants = descendantQuery is not null
            ? await descendantQuery.ToListAsync(ct)
            : [];

        // Process tags ordered by path depth (deepest first) so re-parenting
        // cascades correctly when both parent and child are in the delete set.
        foreach (var tag in tags.OrderByDescending(t => t.Path.Count(c => c == '/')))
        {
            var children = allDescendants
                .Where(t => t.ParentId == tag.Id && !deleteIds.Contains(t.Id))
                .ToList();

            foreach (var child in children)
            {
                var oldChildPath = child.Path;
                child.ParentId = tag.ParentId;

                if (tag.ParentId is null)
                {
                    // Deleted tag was root — child becomes root, strip the deleted segment
                    child.Path = child.Path[(tag.Path.Length + 1)..];
                }
                else
                {
                    var parentPath = tag.Path[..tag.Path.LastIndexOf('/')];
                    child.Path = parentPath + child.Path[tag.Path.Length..];
                }

                // Update deeper descendants in memory
                foreach (var desc in allDescendants.Where(d => d.Path.StartsWith(oldChildPath + "/")))
                    desc.Path = child.Path + desc.Path[oldChildPath.Length..];
            }
        }

        db.Tags.RemoveRange(tags);
        await db.SaveChangesAsync(ct);

        return new BulkDeleteTagsResponse(tags.Count);
    }
}
