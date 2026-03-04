using Homestead.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Persistence;

public class TagPathResolver(AppDbContext db) : ITagPathResolver
{
    public async Task<Tag> ResolveOrCreateAsync(string path, string? leafColor = null, Guid? createdByUserId = null, CancellationToken ct = default)
    {
        var normalizedPath = path.Trim().ToLowerInvariant().Trim('/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            throw new ArgumentException("Tag path cannot be empty.");

        // Build all candidate paths up front and batch-fetch existing tags
        // in a single query. This avoids N+1 round-trips and ensures we see
        // tags added to the change tracker by earlier calls within the same scope.
        var candidatePaths = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
            candidatePaths[i] = string.Join('/', segments[..(i + 1)]);

        var existingTags = await db.Tags
            .Where(t => candidatePaths.Contains(t.Path))
            .ToDictionaryAsync(t => t.Path, ct);

        // Also check the change tracker for tags added but not yet persisted
        foreach (var entry in db.ChangeTracker.Entries<Tag>())
        {
            if (entry.State == EntityState.Added && candidatePaths.Contains(entry.Entity.Path))
                existingTags.TryAdd(entry.Entity.Path, entry.Entity);
        }

        Tag? parent = null;
        Tag? tag = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentPath = candidatePaths[i];
            var isLeaf = i == segments.Length - 1;

            if (existingTags.TryGetValue(currentPath, out tag))
            {
                if (isLeaf && leafColor is not null)
                    tag.Color = leafColor;
            }
            else
            {
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = segments[i],
                    Path = currentPath,
                    ParentId = parent?.Id,
                    Color = isLeaf ? leafColor : null,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = createdByUserId
                };
                db.Tags.Add(tag);
                existingTags[currentPath] = tag;
            }

            parent = tag;
        }

        return tag!;
    }
}
