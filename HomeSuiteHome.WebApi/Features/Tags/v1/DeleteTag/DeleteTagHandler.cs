using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Tags.v1.DeleteTag;

public class DeleteTagHandler(AppDbContext db)
    : ICommandHandler<DeleteTagRequest, DeleteTagResponse?>
{
    public async Task<DeleteTagResponse?> HandleAsync(DeleteTagRequest request, CancellationToken ct)
    {
        var tag = await db.Tags
            .Include(t => t.Children)
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct);

        if (tag is null)
            return null;

        // Re-parent direct children to the deleted tag's parent
        foreach (var child in tag.Children)
        {
            child.ParentId = tag.ParentId;

            // Update child path: replace the deleted tag's path prefix with parent's path
            if (tag.ParentId is null)
            {
                // Deleted tag was root, so child becomes root — strip the deleted segment
                child.Path = child.Path[(tag.Path.Length + 1)..];
            }
            else
            {
                var parentPath = tag.Path[..tag.Path.LastIndexOf('/')];
                child.Path = parentPath + child.Path[tag.Path.Length..];
            }

            // Recursively update descendant paths
            await UpdateDescendantPathsAsync(child, ct);
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);

        return new DeleteTagResponse();
    }

    private async Task UpdateDescendantPathsAsync(Domains.Tags.Tag parent, CancellationToken ct)
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
