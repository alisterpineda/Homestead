using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.BulkDeleteTags;

public class BulkDeleteTagsHandler(AppDbContext db, IDocumentQueryService accessService, IAuditContext audit,
    IUnitOfWork unitOfWork)
{
    public async Task<BulkDeleteTagsResponse> HandleAsync(BulkDeleteTagsCommand request, CancellationToken ct)
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
        IQueryable<Domain.Documents.Tag>? descendantQuery = null;
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

            // Resolve the new parent's EffectiveColor for reparented children
            string? newParentEffectiveColor = null;
            if (tag.ParentId is not null)
            {
                var grandparent = tags.FirstOrDefault(t => t.Id == tag.ParentId)
                    ?? allDescendants.FirstOrDefault(t => t.Id == tag.ParentId);
                if (grandparent is not null)
                    newParentEffectiveColor = grandparent.EffectiveColor;
                else
                    newParentEffectiveColor = await db.Tags.Where(t => t.Id == tag.ParentId)
                        .Select(t => t.EffectiveColor).FirstOrDefaultAsync(ct);
            }

            foreach (var child in children)
            {
                var oldChildPath = child.Path;
                var oldChildPathPrefix = oldChildPath + "/";
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

                // Collect deeper descendants once for both color cascade and path updates
                var deeperDescendants = allDescendants
                    .Where(d => d.Path.StartsWith(oldChildPathPrefix))
                    .ToList();

                // Recompute EffectiveColor for the reparented child and its descendants
                var colorDescendants = deeperDescendants.Where(d => !deleteIds.Contains(d.Id)).ToList();
                Domain.Documents.EffectiveColorResolver.Recompute(child, newParentEffectiveColor, colorDescendants);

                // Update deeper descendant paths in memory
                foreach (var desc in deeperDescendants)
                    desc.Path = child.Path + desc.Path[oldChildPath.Length..];
            }
        }

        foreach (var tag in tags)
            audit.Deleted("Tag", tag.Id, description: tag.Name);

        db.Tags.RemoveRange(tags);

        await unitOfWork.SaveChangesAsync(ct);

        return new BulkDeleteTagsResponse(tags.Count);
    }
}
