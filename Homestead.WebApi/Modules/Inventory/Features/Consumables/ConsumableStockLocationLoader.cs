using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables;

/// <summary>
/// Attaches the resolved per-location <see cref="ConsumableResponse.StockLocations"/> breakdown to a
/// batch of consumable responses. One stock-entry query and one holding-location resolve for the whole
/// batch. The single source of this enrichment — shared by the consumables list and the place-contents
/// drill so both render identical breakdowns; the detail read uses <see cref="StockEntryResponseBuilder"/>
/// directly because it returns a standalone locations list, not an attached <see cref="ConsumableResponse"/>.
/// </summary>
internal static class ConsumableStockLocationLoader
{
    public static async Task<List<ConsumableResponse>> AttachStockLocationsAsync(
        AppDbContext db,
        IItemLocationResolver locationResolver,
        IReadOnlyList<ConsumableResponse> consumables,
        CancellationToken ct)
    {
        if (consumables.Count == 0)
            return [.. consumables];

        var consumableIds = consumables.Select(s => s.Id).ToList();
        var entries = await db.StockEntries.AsNoTracking()
            .Where(e => consumableIds.Contains(e.ItemId))
            .Select(e => new { e.ItemId, Source = new StockEntrySource(e.Id, e.Quantity, e.AreaId, e.ContainerItemId) })
            .ToListAsync(ct);

        var paths = await locationResolver.ResolveHoldingLocationsAsync(
            entries.Select(e => new HoldingLocation(e.Source.AreaId, e.Source.ContainerItemId)).ToList(), ct);
        var entriesByConsumable = entries.ToLookup(e => e.ItemId, e => e.Source);

        return consumables
            .Select(s => s with { StockLocations = StockEntryResponseBuilder.Build(entriesByConsumable[s.Id], paths) })
            .ToList();
    }
}
