using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.GetCategories;

public class GetCategoriesHandler(AppDbContext db, ICategoryPathResolver pathResolver)
{
    public async Task<IReadOnlyList<CategoryResponse>> Handle(GetCategoriesQuery request, CancellationToken ct)
    {
        // User-category paths from the resolver; the two system Uncategorised buckets are appended below
        // (absent from the map) as trivial roots — this view intentionally includes them (unlike /accounts).
        var map = await pathResolver.GetPathMapAsync(ct);

        var query = db.Accounts.AsNoTracking().Where(AccountQueries.IsCategoryAccount);
        if (request.Type is { } type)
            query = query.Where(a => a.AccountType == type);

        var rows = await query
            .Select(a => new { a.Id, a.Name, a.AccountType, a.ParentAccountId, a.IsSystem })
            .ToListAsync(ct);

        return rows
            .Select(a =>
            {
                var info = map.GetValueOrDefault(a.Id) ?? new CategoryPathInfo(a.Name, a.Name, 0);
                return CategoryMapping.ToResponse(a.Id, a.Name, a.AccountType, a.ParentAccountId, a.IsSystem, info);
            })
            .OrderBy(r => r.IsSystem)                                // user categories first, buckets pinned last
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)   // parents sort before their children
            .ToList();
    }
}
