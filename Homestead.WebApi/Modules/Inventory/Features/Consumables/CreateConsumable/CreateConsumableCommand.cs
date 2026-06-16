using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;

/// <summary>
/// Creates a fungible item (a "consumable"). The server hard-codes <c>Fungibility = Fungible</c> and
/// <c>Status = Active</c>. A consumable owns no location and no quantity at creation — stock arrives
/// later through the receive operation, which is the only way quantity enters.
/// </summary>
public record CreateConsumableCommand : ICommand<ConsumableResponse>
{
    public required string Name { get; init; }

    /// <summary>Unit of measure ("screws", "rolls", "gal"). Optional — when omitted, the consumable
    /// has no unit and the frontend renders a generic fallback word at display time.</summary>
    public string? Unit { get; init; }

    /// <summary>Optional global low-stock trigger, compared against total on-hand.</summary>
    public decimal? RestockThreshold { get; init; }

    public string? Notes { get; init; }
}
