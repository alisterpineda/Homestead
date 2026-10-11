using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

public class GetBrandsHandler(AppDbContext db)
{
    public async Task<PaginatedResponse<BrandResponse>> HandleAsync(
        GetBrandsQuery request, CancellationToken ct)
    {
        var query = db.Brands.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(b => b.Name.ToLower().Contains(search));
        }

        // ItemCount is a correlated subquery (Brand has no reverse nav collection, by design), so the
        // projection lives inline here rather than in a static Expression.
        var projected = query.Select(b => new BrandResponse
        {
            Id = b.Id,
            Name = b.Name,
            ItemCount = db.Items.Count(i => i.BrandId == b.Id),
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
        });

        var sortBy = (request.SortBy ?? "name").Trim().ToLowerInvariant();
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        // Name is the tiebreaker for the non-unique columns (ItemCount/UpdatedAt tie constantly — every
        // 0-item brand, every bulk-created batch): without it OFFSET/LIMIT pagination over an arbitrary
        // tie order can skip or repeat rows between page fetches. Name is unique (unique lower(Name)
        // index), so it makes the order total. The Name branches are already deterministic on their own.
        projected = (sortBy, desc) switch
        {
            ("items", false) => projected.OrderBy(b => b.ItemCount).ThenBy(b => b.Name),
            ("items", true) => projected.OrderByDescending(b => b.ItemCount).ThenBy(b => b.Name),
            ("updated", false) => projected.OrderBy(b => b.UpdatedAt).ThenBy(b => b.Name),
            ("updated", true) => projected.OrderByDescending(b => b.UpdatedAt).ThenBy(b => b.Name),
            (_, true) => projected.OrderByDescending(b => b.Name),
            _ => projected.OrderBy(b => b.Name),
        };

        return await projected.ToPaginatedAsync(request, ct);
    }
}
