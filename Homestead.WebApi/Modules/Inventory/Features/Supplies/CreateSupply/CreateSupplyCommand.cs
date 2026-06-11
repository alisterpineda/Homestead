using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Features.Supplies;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.CreateSupply;

/// <summary>
/// Creates a fungible item (a "supply"). The server hard-codes <c>Fungibility = Fungible</c> and
/// <c>Status = Active</c>. A supply owns no location and no quantity at creation — stock arrives
/// later through the receive operation, which is the only way quantity enters.
/// </summary>
public record CreateSupplyCommand : ICommand<SupplyResponse>
{
    public required string Name { get; init; }

    /// <summary>Unit of measure ("screws", "rolls", "gal"). Required for a supply.</summary>
    public required string Unit { get; init; }

    /// <summary>Optional global low-stock trigger, compared against total on-hand.</summary>
    public decimal? RestockThreshold { get; init; }

    public string? Notes { get; init; }
}
