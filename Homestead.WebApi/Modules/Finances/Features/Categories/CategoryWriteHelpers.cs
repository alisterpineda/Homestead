using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Categories;

/// <summary>
/// Shared hierarchy validation for category CRUD (create/patch operate on <see cref="Account"/> rows).
/// </summary>
internal static class CategoryWriteHelpers
{
    // Case-insensitive sibling-name uniqueness within (parent, type). The DB partial unique index
    // (NULLS NOT DISTINCT) is the concurrent-race backstop; this gives a friendly error first.
    public static async Task<bool> SiblingNameTakenAsync(
        AppDbContext db, Guid? parentId, AccountType type, string name, Guid? excludeId, CancellationToken ct)
    {
        var lowered = name.Trim().ToLowerInvariant();
        var q = db.Accounts.Where(a => a.AccountType == type && a.Name.ToLower() == lowered);
        // Split on null vs value to avoid EF's null-parameter pitfall (a captured-null == generates
        // "= @p", which never matches SQL NULL; a literal null generates "IS NULL").
        q = parentId is Guid pid ? q.Where(a => a.ParentAccountId == pid) : q.Where(a => a.ParentAccountId == null);
        if (excludeId is Guid ex) q = q.Where(a => a.Id != ex);
        return await q.AnyAsync(ct);
    }

    // Validates a proposed parent: exists, is a category, same type, and not a system bucket
    // (the Uncategorised buckets are never parents — user roots and buckets both sit at ParentAccountId=null).
    public static async Task ValidateParentAsync(
        AppDbContext db, Guid parentId, AccountType type, CancellationToken ct)
    {
        var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == parentId, ct)
            ?? throw new BusinessRuleException("Parent category not found.");
        if (!parent.AccountType.IsCategory())
            throw new BusinessRuleException("Parent must be a category.");
        if (parent.AccountType != type)
            throw new BusinessRuleException($"Parent category must be of type {type}.");
        if (parent.IsSystem)
            throw new BusinessRuleException("The Uncategorised bucket cannot be a parent.");
    }

    // The set of category ids strictly beneath <paramref name="categoryId"/>, for cycle prevention.
    public static async Task<HashSet<Guid>> GetDescendantIdsAsync(
        AppDbContext db, Guid categoryId, CancellationToken ct)
    {
        var edges = await db.Accounts.AsNoTracking()
            .Where(AccountQueries.IsCategoryAccount)
            .Select(a => new { a.Id, a.ParentAccountId })
            .ToListAsync(ct);

        var childrenByParent = edges
            .Where(e => e.ParentAccountId is not null)
            .GroupBy(e => e.ParentAccountId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

        var descendants = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(categoryId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var kids)) continue;
            foreach (var kid in kids)
                if (descendants.Add(kid)) stack.Push(kid);
        }

        return descendants;
    }
}
