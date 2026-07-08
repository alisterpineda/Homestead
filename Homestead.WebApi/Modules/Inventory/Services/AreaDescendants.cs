using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// The single in-memory area-subtree walk, shared by the resolved-area list filters (durables +
/// consumables). Resolved area is never stored and not SQL-expressible, so the list handlers resolve
/// then filter in memory; this rolls an area up to its descendant set so the filtered list mirrors the
/// (rolled-up) Browse area-card counts. Mirrors <c>GetAreasHandler.TreeOrder</c> and
/// <c>ItemLocationResolver</c>'s area-parent climb — kept here as the one copy rather than a third.
/// </summary>
public static class AreaDescendants
{
    /// <summary>
    /// The inclusive subtree of <paramref name="rootId"/>: the area itself plus every descendant, via
    /// a cycle-guarded DFS over the child map built from <paramref name="parentByArea"/> (area id →
    /// its parent area id, null for a root). Pure and allocation-light for unit testing.
    /// </summary>
    public static HashSet<Guid> InclusiveSubtree(Guid rootId, IReadOnlyDictionary<Guid, Guid?> parentByArea)
    {
        var childrenByParent = new Dictionary<Guid, List<Guid>>();
        foreach (var (id, parent) in parentByArea)
        {
            if (parent is not { } p)
                continue;
            if (!childrenByParent.TryGetValue(p, out var list))
                childrenByParent[p] = list = [];
            list.Add(id);
        }

        var result = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!result.Add(current)) // cycle guard: a revisited node is already in the set
                continue;
            if (childrenByParent.TryGetValue(current, out var children))
                foreach (var child in children)
                    stack.Push(child);
        }

        return result;
    }

    /// <summary>Loads the (household-scale) area parent map and computes the inclusive subtree.</summary>
    public static async Task<HashSet<Guid>> InclusiveSubtreeAsync(AppDbContext db, Guid rootId, CancellationToken ct)
    {
        var parentByArea = await db.Areas.AsNoTracking()
            .Select(a => new { a.Id, a.ParentAreaId })
            .ToDictionaryAsync(a => a.Id, a => a.ParentAreaId, ct);
        return InclusiveSubtree(rootId, parentByArea);
    }
}
