using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupplies;

public class GetSuppliesHandler(AppDbContext db)
    : IRequestHandler<GetSuppliesQuery, PaginatedResponse<SupplyResponse>>
{
    public async Task<PaginatedResponse<SupplyResponse>> Handle(
        GetSuppliesQuery request, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.Fungible);

        // A supply's only terminal status is Archived; the live filter hides it by default.
        if (request.IncludeInactive != true)
            query = query.Where(i => i.Status == ItemStatus.Active);

        if (request.LowStockOnly == true)
            query = query.Where(i =>
                i.RestockThreshold != null && i.StockEntries.Sum(e => e.Quantity) <= i.RestockThreshold);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(search));
        }

        return await query
            .OrderBy(i => i.Name)
            .Select(SupplyResponseMapper.Projection)
            .ToPaginatedAsync(request, ct);
    }
}
