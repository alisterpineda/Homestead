using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Features.Durables;

namespace Homestead.WebApi.Modules.Inventory.Features.Items;

/// <summary>
/// Unified detail envelope for a single <see cref="Item"/> row, discriminated by
/// <see cref="Fungibility"/>. Exactly one of <see cref="Durable"/> / <see cref="Consumable"/> is set,
/// matching the discriminator. Lets the frontend land on <c>/inventory/items/{id}</c> without knowing
/// the kind up front and preload the correct child view in one round-trip.
/// </summary>
public record ItemDetailResponse
{
    public required Guid Id { get; init; }

    /// <summary>Discriminator; selects which of the two payloads below is populated.</summary>
    public required Fungibility Fungibility { get; init; }

    /// <summary>Set when <see cref="Fungibility"/> is <see cref="Fungibility.NonFungible"/>.</summary>
    public DurableResponse? Durable { get; init; }

    /// <summary>Set when <see cref="Fungibility"/> is <see cref="Fungibility.Fungible"/>.</summary>
    public ConsumableDetailResponse? Consumable { get; init; }
}
