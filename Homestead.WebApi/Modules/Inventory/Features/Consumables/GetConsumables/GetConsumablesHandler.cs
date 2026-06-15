using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;

public class GetConsumablesHandler(AppDbContext db, IItemLocationResolver locationResolver)
    : IRequestHandler<GetConsumablesQuery, PaginatedResponse<ConsumableResponse>>
{
    public async Task<PaginatedResponse<ConsumableResponse>> Handle(
        GetConsumablesQuery request, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.Fungible);

        // A consumable's only terminal status is Archived; the live filter hides it by default.
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

        var page = await query
            .OrderBy(i => i.Name)
            .Select(ConsumableResponseMapper.Projection)
            .ToPaginatedAsync(request, ct);

        // Attach the resolved per-location breakdown for the page's consumables — same post-query
        // enrichment pattern as GetDurablesHandler.
        return page with
        {
            Items = await ConsumableStockLocationLoader.AttachStockLocationsAsync(db, locationResolver, page.Items, ct),
        };
    }
}
