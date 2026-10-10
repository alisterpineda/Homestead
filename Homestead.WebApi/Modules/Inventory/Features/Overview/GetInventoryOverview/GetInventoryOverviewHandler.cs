using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public class GetInventoryOverviewHandler(
    AppDbContext db, IInventoryPlacementService placementService, IItemLocationResolver locationResolver,
    TimeProvider timeProvider)
{
    private const int PreviewCount = 5;
    private const int RecentCount = 10;

    public async Task<GetInventoryOverviewResponse> Handle(
        GetInventoryOverviewQuery request, CancellationToken ct)
    {
        // Low stock — same predicate as GetConsumablesHandler's LowStockOnly filter over live
        // consumables. The on-hand sum is projected once so SQL evaluates the correlated aggregate
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
        var lowStockConsumables = await lowStock
            .OrderBy(s => s.Threshold == 0 ? 0 : s.OnHand / s.Threshold)
            .ThenBy(s => s.Name)
            .Take(PreviewCount)
            .Select(s => new LowStockConsumablePreview
            {
                Id = s.Id,
                Name = s.Name,
                TotalOnHand = s.OnHand,
                Unit = s.Unit,
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
            .Select(i => new LentDurablePreview { Id = i.Id, Name = i.Name, StatusNote = i.StatusNote })
            .ToListAsync(ct);

        // Warranty expiring — the item-level Expiring rule of WarrantyStatusRules, in SQL. The
        // "no open-ended row" guard is what keeps a lifetime warranty from ever counting as expiring,
        // matching DurableResponseMapper's projection; the latest expiry is what decides, so a lapsed
        // narrow tier alongside live cover doesn't raise the tile.
        var today = WarrantyStatusRules.TodayUtc(timeProvider);
        var cutoff = WarrantyStatusRules.Cutoff(today);
        var expiringWarranties = db.Items.AsNoTracking()
            .Where(i => i.Fungibility == Fungibility.NonFungible
                && ItemStatusExtensions.LiveStatuses.Contains(i.Status)
                && i.Warranties.Any()
                && !i.Warranties.Any(w => w.ExpiresOn == null))
            .Select(i => new
            {
                i.Id,
                i.Name,
                Latest = i.Warranties.Max(w => w.ExpiresOn),
            })
            .Where(x => x.Latest >= today && x.Latest <= cutoff);

        var expiringWarrantyCount = await expiringWarranties.CountAsync(ct);
        var expiringWarrantyPreviews = await expiringWarranties
            .OrderBy(x => x.Latest)
            .ThenBy(x => x.Name)
            .Take(PreviewCount)
            .Select(x => new ExpiringWarrantyPreview(x.Id, x.Name, x.Latest!.Value))
            .ToListAsync(ct);

        var placement = await placementService.ComputeAsync(ct);

        // SQL ordering for collation parity with GetAreasHandler. Zero-item areas are included —
        // an empty area is still a browse target.
        var areas = await db.Areas.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.ParentAreaId })
            .ToListAsync(ct);

        var areaSummaries = areas
            .Select(a => new AreaSummary
            {
                Id = a.Id,
                Name = a.Name,
                ParentAreaId = a.ParentAreaId,
                ItemCount = placement.AreaItemCounts.GetValueOrDefault(a.Id),
                LowStockCount = placement.AreaLowStockCounts.GetValueOrDefault(a.Id),
            })
            .ToList();

        var recentlyUpdated = await BuildRecentlyUpdatedAsync(today, ct);

        return new GetInventoryOverviewResponse
        {
            LowStockCount = lowStockCount,
            LowStockConsumables = lowStockConsumables,
            LentCount = lentCount,
            LentDurables = lentItems,
            UnplacedCount = placement.UnplacedItems.Count,
            ExpiringWarrantyCount = expiringWarrantyCount,
            ExpiringWarranties = expiringWarrantyPreviews,
            Areas = areaSummaries,
            RecentlyUpdated = recentlyUpdated,
        };
    }

    /// <summary>
    /// The browse-root "Recently updated" strip: the <see cref="RecentCount"/> most-recently-touched
    /// live items, newest first. Recency is the effective last-activity time — the later of
    /// <c>Item.UpdatedAt</c> and (for a consumable) its latest <c>StockMovement.CreatedAt</c>, so a
    /// restock/move/consume bumps a consumable even though it only touches its stock rows — and a
    /// consume-to-zero still counts, because the ledger row survives the entry's purge.
    /// </summary>
    /// <remarks>
    /// Materialize-then-merge in memory rather than gamble on EF translating a CASE-over-correlated-MAX
    /// in <c>ORDER BY</c>; the lightweight tuple scan is consistent with the placement service's item
    /// scan. The winners are then re-projected by id through the shared mappers and both kinds arrive
    /// location-enriched: durables via <see cref="IItemLocationResolver"/>, consumables via
    /// <see cref="ConsumableStockLocationLoader"/>, so each recency row renders its location with no
    /// re-fetch (the consumable quick panel still re-fetches its full breakdown on open — the row only
    /// needs display text).
    /// </remarks>
    private async Task<IReadOnlyList<RecentlyUpdatedItem>> BuildRecentlyUpdatedAsync(
        DateOnly today, CancellationToken ct)
    {
        // Consumables are only ever Active among the live statuses; durables span Active/Lent.
        var recency = await db.Items.AsNoTracking()
            .Where(i => ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .Select(i => new
            {
                i.Id,
                i.Fungibility,
                i.UpdatedAt,
                // Derive fungible recency from the append-only movement ledger, not the stock entry:
                // running a consumable out DELETES its entry, so MAX(entry.UpdatedAt) would drop the
                // just-emptied item off the strip. CreatedAt (record-time, not back-datable OccurredAt)
                // survives the purge and is written by every op. Null when no movements.
                LastStockAt = i.Fungibility == Fungibility.Fungible
                    ? db.StockMovements.Where(m => m.ItemId == i.Id).Max(m => (DateTimeOffset?)m.CreatedAt)
                    : null,
            })
            .ToListAsync(ct);

        var winners = recency
            .Select(r => new
            {
                r.Id,
                r.Fungibility,
                Effective = r.LastStockAt > r.UpdatedAt ? r.LastStockAt.Value : r.UpdatedAt,
            })
            .OrderByDescending(r => r.Effective)
            .ThenBy(r => r.Id)
            .Take(RecentCount)
            .ToList();

        if (winners.Count == 0)
            return [];

        var durableIds = winners.Where(w => w.Fungibility == Fungibility.NonFungible).Select(w => w.Id).ToList();
        var consumableIds = winners.Where(w => w.Fungibility == Fungibility.Fungible).Select(w => w.Id).ToList();

        // Re-apply the live-status filter on the re-projection — not just an id match. The winners are
        // live as of the scan, but this is a separate query with no transaction, so a concurrent flip
        // to a terminal status (or a delete) between the scan and here must drop the row rather than
        // surface an item the rest of the inventory now hides. A dropped row is then skipped below.
        IReadOnlyList<DurableResponse> durables = [];
        if (durableIds.Count > 0)
        {
            var projected = await db.Items.AsNoTracking()
                .Where(i => durableIds.Contains(i.Id) && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
                // Same "today" as the expiring tile above, so one response never carries two clocks.
                .Select(DurableResponseMapper.For(today))
                .ToListAsync(ct);
            durables = await locationResolver.EnrichAsync(projected, ct);
        }

        List<ConsumableResponse> consumables = [];
        if (consumableIds.Count > 0)
        {
            consumables = await db.Items.AsNoTracking()
                .Where(i => consumableIds.Contains(i.Id) && ItemStatusExtensions.LiveStatuses.Contains(i.Status))
                .Select(ConsumableResponseMapper.Projection)
                .ToListAsync(ct);
            consumables = await ConsumableStockLocationLoader.AttachStockLocationsAsync(db, locationResolver, consumables, ct);
        }

        var durableById = durables.ToDictionary(d => d.Id);
        var consumableById = consumables.ToDictionary(c => c.Id);

        // Reassemble in the step-2 display order, wrapping each full DTO with its effective time. A
        // winner whose row vanished from the re-projection (concurrent delete/terminalize) is skipped
        // via TryGetValue rather than indexed — a lost row degrades the strip gracefully, never 500s.
        var result = new List<RecentlyUpdatedItem>(winners.Count);
        foreach (var w in winners)
        {
            if (w.Fungibility == Fungibility.NonFungible)
            {
                if (durableById.TryGetValue(w.Id, out var durable))
                    result.Add(new RecentlyUpdatedItem { LastActivityAt = w.Effective, Durable = durable });
            }
            else if (consumableById.TryGetValue(w.Id, out var consumable))
            {
                result.Add(new RecentlyUpdatedItem { LastActivityAt = w.Effective, Consumable = consumable });
            }
        }

        return result;
    }
}
