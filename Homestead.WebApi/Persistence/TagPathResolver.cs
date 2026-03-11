using Homestead.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Persistence;

public class TagPathResolver(AppDbContext db) : ITagPathResolver
{
    public async Task<Tag> ResolveOrCreateAsync(string path, string? leafColor = null, Guid? createdByUserId = null, CancellationToken ct = default)
    {
        var (segments, candidatePaths) = NormalizeAndBuildCandidatePaths(path);

        var existingTags = await db.Tags
            .Where(t => candidatePaths.Contains(t.Path))
            .ToDictionaryAsync(t => t.Path, ct);

        MergeChangeTrackerEntries(candidatePaths, existingTags);

        Tag? parent = null;
        Tag? tag = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentPath = candidatePaths[i];
            var isLeaf = i == segments.Length - 1;

            if (existingTags.TryGetValue(currentPath, out tag))
            {
                if (isLeaf && leafColor is not null)
                {
                    tag.Color = leafColor;
                    tag.EffectiveColor = leafColor;
                }
            }
            else
            {
                var color = isLeaf ? leafColor : null;
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = segments[i],
                    Path = currentPath,
                    ParentId = parent?.Id,
                    Color = color,
                    EffectiveColor = color ?? parent?.EffectiveColor,
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

    public async Task<List<Tag>> ResolveOrCreateManyAsync(List<string> paths, Guid? createdByUserId = null, CancellationToken ct = default)
    {
        if (paths.Count == 0)
            return [];

        var normalizedPaths = new List<(string[] Segments, string[] CandidatePaths)>(paths.Count);
        foreach (var path in paths)
            normalizedPaths.Add(NormalizeAndBuildCandidatePaths(path));

        // Collect all candidate paths into a single set for one DB query
        var allCandidatePaths = new HashSet<string>();
        foreach (var (_, candidatePaths) in normalizedPaths)
        {
            foreach (var cp in candidatePaths)
                allCandidatePaths.Add(cp);
        }

        var existingTags = await db.Tags
            .Where(t => allCandidatePaths.Contains(t.Path))
            .ToDictionaryAsync(t => t.Path, ct);

        MergeChangeTrackerEntries(allCandidatePaths, existingTags);

        var results = new List<Tag>(paths.Count);
        foreach (var (segments, candidatePaths) in normalizedPaths)
        {
            Tag? parent = null;
            Tag? tag = null;

            for (var i = 0; i < segments.Length; i++)
            {
                var currentPath = candidatePaths[i];

                if (!existingTags.TryGetValue(currentPath, out tag))
                {
                    tag = new Tag
                    {
                        Id = Guid.NewGuid(),
                        Name = segments[i],
                        Path = currentPath,
                        ParentId = parent?.Id,
                        Color = null,
                        EffectiveColor = parent?.EffectiveColor,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedByUserId = createdByUserId
                    };
                    db.Tags.Add(tag);
                    existingTags[currentPath] = tag;
                }

                parent = tag;
            }

            results.Add(tag!);
        }

        return results;
    }

    private static (string[] Segments, string[] CandidatePaths) NormalizeAndBuildCandidatePaths(string path)
    {
        var normalizedPath = path.Trim().Trim('/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            throw new ArgumentException("Tag path cannot be empty.");

        var candidatePaths = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
            candidatePaths[i] = string.Join('/', segments[..(i + 1)]);

        return (segments, candidatePaths);
    }

    private void MergeChangeTrackerEntries(IEnumerable<string> candidatePaths, Dictionary<string, Tag> existingTags)
    {
        foreach (var entry in db.ChangeTracker.Entries<Tag>())
        {
            if (entry.State == EntityState.Added && candidatePaths.Contains(entry.Entity.Path))
                existingTags.TryAdd(entry.Entity.Path, entry.Entity);
        }
    }
}
