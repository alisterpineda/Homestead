using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class CategoryPathResolver(AppDbContext db) : ICategoryPathResolver
{
    // Breadcrumb separator — a right-pointing angle quote, matching the category design's "Food › Groceries".
    private const string Separator = " › ";

    public async Task<IReadOnlyDictionary<Guid, CategoryPathInfo>> GetPathMapAsync(CancellationToken ct)
    {
        // One load of all user category accounts; paths built in memory. Categories are few (dozens),
        // so a full scan is cheaper than per-row recursive DB walks.
        var nodes = await db.Accounts
            .AsNoTracking()
            .Where(AccountQueries.IsCategoryAccount)
            .Where(a => !a.IsSystem)
            .Select(a => new NodeRow(a.Id, a.Name, a.ParentAccountId))
            .ToListAsync(ct);

        var byId = nodes.ToDictionary(n => n.Id);
        var map = new Dictionary<Guid, CategoryPathInfo>(nodes.Count);

        foreach (var node in nodes)
        {
            var segments = new List<string>();
            var currentId = (Guid?)node.Id;
            var seen = new HashSet<Guid>();

            // Walk up the parent chain. `seen` guards against a corrupt cycle looping forever — the
            // write path (PatchCategory) already forbids cycles, but a read must never hang on bad data.
            while (currentId is Guid id && byId.TryGetValue(id, out var current) && seen.Add(id))
            {
                segments.Add(current.Name);
                currentId = current.ParentAccountId;
            }

            segments.Reverse();
            map[node.Id] = new CategoryPathInfo(node.Name, string.Join(Separator, segments), segments.Count - 1);
        }

        return map;
    }

    private readonly record struct NodeRow(Guid Id, string Name, Guid? ParentAccountId);
}
