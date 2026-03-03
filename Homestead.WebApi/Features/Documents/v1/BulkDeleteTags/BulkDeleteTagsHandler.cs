using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;

public class BulkDeleteTagsHandler(AppDbContext db, IDocumentQueryService accessService)
    : ICommandHandler<BulkDeleteTagsRequest, BulkDeleteTagsResponse>
{
    public async Task<BulkDeleteTagsResponse> HandleAsync(BulkDeleteTagsRequest request, CancellationToken ct)
    {
        var tags = await db.Tags
            .Include(t => t.Children)
            .Where(t => request.Ids.Contains(t.Id))
            .ToListAsync(ct);

        if (tags.Count == 0)
            return new BulkDeleteTagsResponse(0);

        // Block deletion if any requested tags are used on inaccessible documents
        var relatedDocIds = db.DocumentTags
            .Where(dt => request.Ids.Contains(dt.TagId))
            .Select(dt => dt.DocumentId);

        await accessService.EnsureAllDocumentsAccessibleAsync(relatedDocIds, ct);

        var deleteIds = new HashSet<Guid>(request.Ids);

        // Process tags ordered by path depth (deepest first) so re-parenting
        // cascades correctly when both parent and child are in the delete set.
        foreach (var tag in tags.OrderByDescending(t => t.Path.Count(c => c == '/')))
        {
            // Re-query current children in case a prior iteration already re-parented some
            var children = await db.Tags
                .Where(t => t.ParentId == tag.Id && !deleteIds.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var child in children)
            {
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

                await UpdateDescendantPathsAsync(child, ct);
            }
        }

        db.Tags.RemoveRange(tags);
        await db.SaveChangesAsync(ct);

        return new BulkDeleteTagsResponse(tags.Count);
    }

    private async Task UpdateDescendantPathsAsync(Tag parent, CancellationToken ct)
    {
        var children = await db.Tags
            .Where(t => t.ParentId == parent.Id)
            .ToListAsync(ct);

        foreach (var child in children)
        {
            child.Path = parent.Path + "/" + child.Name;
            await UpdateDescendantPathsAsync(child, ct);
        }
    }
}
