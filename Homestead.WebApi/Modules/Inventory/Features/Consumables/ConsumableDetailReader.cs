using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables;

/// <summary>
/// Loads a consumable plus its resolved per-location breakdown. Shared by the detail query and the four
/// stock operations, which each return the refreshed detail so the UI updates in one round-trip.
/// </summary>
internal static class ConsumableDetailReader
{
    public static async Task<ConsumableDetailResponse?> LoadAsync(
        AppDbContext db, IItemLocationResolver locationResolver, Guid id, CancellationToken ct)
    {
        var consumable = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == id && i.Fungibility == Fungibility.Fungible)
            .Select(ConsumableResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);

        if (consumable is null)
            return null;

        var entries = await db.StockEntries.AsNoTracking()
            .Where(e => e.ItemId == id)
            .Select(e => new StockEntrySource(e.Id, e.Quantity, e.AreaId, e.ContainerItemId))
            .ToListAsync(ct);

        var paths = await locationResolver.ResolveHoldingLocationsAsync(
            entries.Select(e => new HoldingLocation(e.AreaId, e.ContainerItemId)).ToList(), ct);

        return new ConsumableDetailResponse
        {
            Consumable = consumable,
            Locations = StockEntryResponseBuilder.Build(entries, paths),
        };
    }
}
