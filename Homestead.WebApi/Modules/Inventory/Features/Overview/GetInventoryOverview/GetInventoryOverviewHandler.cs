using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public class GetInventoryOverviewHandler(AppDbContext db, IInventoryPlacementService placementService)
    : IRequestHandler<GetInventoryOverviewQuery, GetInventoryOverviewResponse>
{
    private const int PreviewCount = 5;

    public async Task<GetInventoryOverviewResponse> Handle(
        GetInventoryOverviewQuery request, CancellationToken ct)
    {
        // Low stock — same predicate as GetSuppliesHandler's LowStockOnly filter over live
        // supplies. The on-hand sum is projected once so SQL evaluates the correlated aggregate
        // a single time, not per clause.
        var lowStock = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.Fungible
                && i.Status == ItemStatus.Active
                && i.RestockThreshold != null)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Unit,
                Threshold = i.RestockThreshold!.Value,
                OnHand = i.StockEntries.Sum(e => e.Quantity),
            })
            .Where(s => s.OnHand <= s.Threshold);

        var lowStockCount = await lowStock.CountAsync(ct);
        // Most urgent first = lowest on-hand/threshold ratio. A zero threshold only matches when
        // on-hand is 0 (quantity sums are >= the threshold floor of 0), i.e. fully out — rank it
        // most urgent without dividing.
        var lowStockSupplies = await lowStock
            .OrderBy(s => s.Threshold == 0 ? 0 : s.OnHand / s.Threshold)
            .ThenBy(s => s.Name)
            .Take(PreviewCount)
            .Select(s => new LowStockSupplyPreview
            {
                Id = s.Id,
                Name = s.Name,
                TotalOnHand = s.OnHand,
                Unit = s.Unit!,
                RestockThreshold = s.Threshold,
            })
            .ToListAsync(ct);

        // Lent — a live status, so no terminal exclusion needed beyond the status match itself.
        var lent = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible && i.Status == ItemStatus.Lent);

        var lentCount = await lent.CountAsync(ct);
        var lentItems = await lent
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Id)
            .Take(PreviewCount)
            .Select(i => new LentItemPreview { Id = i.Id, Name = i.Name, LentTo = i.LentTo })
            .ToListAsync(ct);

        var placement = await placementService.ComputeAsync(ct);

        // SQL ordering for collation parity with GetAreasHandler. Zero-item areas are included —
        // an empty area is still a browse target.
        var areas = await db.Areas.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);

        var areaSummaries = areas
            .Select(a => new AreaSummary
            {
                Id = a.Id,
                Name = a.Name,
                ItemCount = placement.AreaItemCounts.GetValueOrDefault(a.Id),
                LowStockCount = placement.AreaLowStockCounts.GetValueOrDefault(a.Id),
            })
            .ToList();

        var unplacedItems = placement.UnplacedItems
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PreviewCount)
            .Select(i => new UnplacedItemPreview { Id = i.Id, Name = i.Name })
            .ToList();

        return new GetInventoryOverviewResponse
        {
            LowStockCount = lowStockCount,
            LowStockSupplies = lowStockSupplies,
            LentCount = lentCount,
            LentItems = lentItems,
            UnplacedCount = placement.UnplacedItems.Count,
            UnplacedItems = unplacedItems,
            Areas = areaSummaries,
        };
    }
}
