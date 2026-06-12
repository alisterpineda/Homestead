using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupplies;

public class GetSuppliesHandler(AppDbContext db, IItemLocationResolver locationResolver)
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

        var page = await query
            .OrderBy(i => i.Name)
            .Select(SupplyResponseMapper.Projection)
            .ToPaginatedAsync(request, ct);

        // Attach the resolved per-location breakdown for the page's supplies — same post-query
        // enrichment pattern as GetItemsHandler.
        if (page.Items.Count == 0)
            return page;

        var supplyIds = page.Items.Select(s => s.Id).ToList();
        var entries = await db.StockEntries.AsNoTracking()
            .Where(e => supplyIds.Contains(e.ItemId))
            .Select(e => new { e.ItemId, Source = new StockEntrySource(e.Id, e.Quantity, e.AreaId, e.ContainerItemId) })
            .ToListAsync(ct);

        var paths = await locationResolver.ResolveHoldingLocationsAsync(
            entries.Select(e => new HoldingLocation(e.Source.AreaId, e.Source.ContainerItemId)).ToList(), ct);

        var entriesBySupply = entries.ToLookup(e => e.ItemId, e => e.Source);

        return page with
        {
            Items = page.Items
                .Select(s => s with
                {
                    StockLocations = StockEntryResponseBuilder.Build(entriesBySupply[s.Id], paths),
                })
                .ToList(),
        };
    }
}
