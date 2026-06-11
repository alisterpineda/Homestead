using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>
/// Loads a supply plus its resolved per-location breakdown. Shared by the detail query and the four
/// stock operations, which each return the refreshed detail so the UI updates in one round-trip.
/// </summary>
internal static class SupplyDetailReader
{
    public static async Task<SupplyDetailResponse?> LoadAsync(
        AppDbContext db, IItemLocationResolver locationResolver, Guid id, CancellationToken ct)
    {
        var supply = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == id && i.Fungibility == Fungibility.Fungible)
            .Select(SupplyResponseMapper.Projection)
            .FirstOrDefaultAsync(ct);

        if (supply is null)
            return null;

        var entries = await db.StockEntries.AsNoTracking()
            .Where(e => e.ItemId == id)
            .Select(e => new { e.Id, e.Quantity, e.AreaId, e.ContainerItemId })
            .ToListAsync(ct);

        var paths = await locationResolver.ResolveHoldingLocationsAsync(
            entries.Select(e => new HoldingLocation(e.AreaId, e.ContainerItemId)).ToList(), ct);

        var locations = entries
            .Select(e =>
            {
                var path = paths.GetValueOrDefault(
                    new HoldingLocation(e.AreaId, e.ContainerItemId), LocationPath.Empty);
                return new StockEntryResponse
                {
                    Id = e.Id,
                    Quantity = e.Quantity,
                    AreaId = e.AreaId,
                    ContainerItemId = e.ContainerItemId,
                    Path = path.Segments,
                    LocationLabel = path.Label,
                };
            })
            .OrderByDescending(e => e.Quantity)
            .ThenBy(e => e.LocationLabel)
            .ToList();

        return new SupplyDetailResponse { Supply = supply, Locations = locations };
    }
}
