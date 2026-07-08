using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;

/// <summary>
/// Creates a fungible item (a "consumable"). The server hard-codes <c>Fungibility = Fungible</c> and
/// <c>Status = Active</c>. A consumable owns no location; stock normally arrives later through the
/// receive operation. As a convenience, an optional <see cref="InitialQuantity"/> at a starting
/// location (exactly one of <see cref="InitialAreaId"/> / <see cref="InitialContainerItemId"/>) seeds
/// that quantity at creation time, received in the same transaction as a real inflow movement.
/// </summary>
public record CreateConsumableCommand : ICommand<ConsumableResponse>
{
    public required string Name { get; init; }

    /// <summary>Unit of measure ("screws", "rolls", "gal"). Optional — when omitted, the consumable
    /// has no unit and the frontend renders a generic fallback word at display time.</summary>
    public string? Unit { get; init; }

    /// <summary>Optional global low-stock trigger, compared against total on-hand.</summary>
    public decimal? RestockThreshold { get; init; }

    /// <summary>Optional canonical brand this consumable is attributed to.</summary>
    public Guid? BrandId { get; init; }

    public string? Notes { get; init; }

    /// <summary>Optional starting location (area) for seeded stock. Set together with
    /// <see cref="InitialQuantity"/>, and mutually exclusive with <see cref="InitialContainerItemId"/>.</summary>
    public Guid? InitialAreaId { get; init; }

    /// <summary>Optional starting location (container item) for seeded stock. Set together with
    /// <see cref="InitialQuantity"/>, and mutually exclusive with <see cref="InitialAreaId"/>.</summary>
    public Guid? InitialContainerItemId { get; init; }

    /// <summary>Optional starting quantity to receive at the starting location on create. When set,
    /// exactly one of <see cref="InitialAreaId"/> / <see cref="InitialContainerItemId"/> must be set.</summary>
    public decimal? InitialQuantity { get; init; }

    /// <summary>
    /// Slash-delimited classification tag paths. Missing ancestors are upserted; the item's tag set is
    /// replaced with exactly these. Omit or empty ⇒ no tags.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }
}
