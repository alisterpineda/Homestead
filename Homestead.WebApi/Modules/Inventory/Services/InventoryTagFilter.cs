using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// Shared tag-filter expansion for the two item-list handlers (durables + consumables).
/// Ports the descendant expansion from <c>GetDocumentsHandler.BuildScopedQueryAsync</c>:
/// with <paramref name="includeChildTags"/> each selected tag's subtree
/// (<c>Path.StartsWith(path + "/")</c>) is unioned in.
/// </summary>
public static class InventoryTagFilter
{
    public static async Task<IReadOnlyList<Guid>> ExpandTagIdsAsync(
        AppDbContext db, Guid[] tagIds, bool includeChildTags, CancellationToken ct)
    {
        if (tagIds.Length == 0)
            return [];

        if (!includeChildTags)
            return tagIds;

        var selectedPaths = await db.InventoryTags.AsNoTracking()
            .Where(t => tagIds.Contains(t.Id))
            .Select(t => t.Path)
            .ToListAsync(ct);

        var expandedTagIds = db.InventoryTags.AsNoTracking()
            .Where(t => tagIds.Contains(t.Id));

        foreach (var path in selectedPaths)
        {
            expandedTagIds = expandedTagIds.Union(
                db.InventoryTags.AsNoTracking().Where(t => t.Path.StartsWith(path + "/")));
        }

        return await expandedTagIds.Select(t => t.Id).ToListAsync(ct);
    }
}
