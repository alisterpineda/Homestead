using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public class GetInventoryOverviewHandler(AppDbContext db)
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

        var (unplacedCount, unplacedItems) = await ComputeUnplacedAsync(ct);

        return new GetInventoryOverviewResponse
        {
            LowStockCount = lowStockCount,
            LowStockSupplies = lowStockSupplies,
            LentCount = lentCount,
            LentItems = lentItems,
            UnplacedCount = unplacedCount,
            UnplacedItems = unplacedItems,
        };
    }

    /// <summary>
    /// Resolved-Unplaced semantics: an item is unplaced when no area is reachable up its container
    /// chain — including items nested inside an unplaced container. This mirrors the parent-chain
    /// walk in <see cref="Services.ItemLocationResolver"/> (broken or cyclic chain ⇒ unplaced),
    /// which must match what the deep-linked items list shows; keep the two in sync.
    /// </summary>
    private async Task<(int Count, IReadOnlyList<UnplacedItemPreview> Preview)> ComputeUnplacedAsync(
        CancellationToken ct)
    {
        // Every valid ancestor is a container; deliberately not status-filtered (a chain may pass
        // through terminal containers), same as the resolver.
        var containers = await db.Items.AsNoTracking()
            .Where(i => i.IsContainer)
            .Select(i => new { i.Id, i.AreaId, i.ParentItemId })
            .ToDictionaryAsync(c => c.Id, ct);

        // A directly-placed item can never be resolved-unplaced, so filter those out in SQL.
        var candidates = await db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible
                && i.AreaId == null
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .Select(i => new { i.Id, i.Name, i.ParentItemId })
            .ToListAsync(ct);

        var unplaced = new List<UnplacedItemPreview>();
        foreach (var item in candidates)
        {
            var isUnplaced = true;
            var visited = new HashSet<Guid> { item.Id };
            var currentId = item.ParentItemId;
            while (currentId is { } id)
            {
                // Broken or cyclic chain ⇒ unplaced from this point.
                if (!visited.Add(id) || !containers.TryGetValue(id, out var node))
                    break;

                if (node.AreaId is not null)
                {
                    isUnplaced = false;
                    break;
                }

                currentId = node.ParentItemId;
            }

            if (isUnplaced)
                unplaced.Add(new UnplacedItemPreview { Id = item.Id, Name = item.Name });
        }

        var preview = unplaced
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PreviewCount)
            .ToList();

        return (unplaced.Count, preview);
    }
}
