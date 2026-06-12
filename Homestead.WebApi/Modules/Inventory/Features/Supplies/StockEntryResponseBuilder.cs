using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>The raw stock-entry fields needed to build a <see cref="StockEntryResponse"/>.</summary>
internal sealed record StockEntrySource(Guid Id, decimal Quantity, Guid? AreaId, Guid? ContainerItemId);

/// <summary>
/// Maps raw stock entries plus their resolved holding-location paths into
/// <see cref="StockEntryResponse"/>s, largest quantity first. Shared by the supply detail reader
/// and the supplies list so both render locations identically.
/// </summary>
internal static class StockEntryResponseBuilder
{
    public static List<StockEntryResponse> Build(
        IEnumerable<StockEntrySource> entries,
        IReadOnlyDictionary<HoldingLocation, LocationPath> paths)
    {
        return entries
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
    }
}
