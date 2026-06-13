using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>
/// Attaches the resolved per-location <see cref="SupplyResponse.StockLocations"/> breakdown to a
/// batch of supply responses. One stock-entry query and one holding-location resolve for the whole
/// batch. The single source of this enrichment — shared by the supplies list and the place-contents
/// drill so both render identical breakdowns; the detail read uses <see cref="StockEntryResponseBuilder"/>
/// directly because it returns a standalone locations list, not an attached <see cref="SupplyResponse"/>.
/// </summary>
internal static class SupplyStockLocationLoader
{
    public static async Task<List<SupplyResponse>> AttachStockLocationsAsync(
        AppDbContext db,
        IItemLocationResolver locationResolver,
        IReadOnlyList<SupplyResponse> supplies,
        CancellationToken ct)
    {
        if (supplies.Count == 0)
            return [.. supplies];

        var supplyIds = supplies.Select(s => s.Id).ToList();
        var entries = await db.StockEntries.AsNoTracking()
            .Where(e => supplyIds.Contains(e.ItemId))
            .Select(e => new { e.ItemId, Source = new StockEntrySource(e.Id, e.Quantity, e.AreaId, e.ContainerItemId) })
            .ToListAsync(ct);

        var paths = await locationResolver.ResolveHoldingLocationsAsync(
            entries.Select(e => new HoldingLocation(e.Source.AreaId, e.Source.ContainerItemId)).ToList(), ct);
        var entriesBySupply = entries.ToLookup(e => e.ItemId, e => e.Source);

        return supplies
            .Select(s => s with { StockLocations = StockEntryResponseBuilder.Build(entriesBySupply[s.Id], paths) })
            .ToList();
    }
}
