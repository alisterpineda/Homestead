using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

/// <summary>
/// Body of a track-one-individually: carves a single unit out of fungible stock at one location
/// (exactly one of <see cref="AreaId"/> / <see cref="ContainerItemId"/>) into a new non-fungible item.
/// </summary>
public record TrackDurableRequest
{
    /// <summary>Source area. Exactly one of this / <see cref="ContainerItemId"/>.</summary>
    public Guid? AreaId { get; init; }

    /// <summary>Source container. Exactly one of this / <see cref="AreaId"/>.</summary>
    public Guid? ContainerItemId { get; init; }

    /// <summary>Name for the new individual item; the client prefills the consumable's name.</summary>
    public required string Name { get; init; }

    /// <summary>Free-text sub-position; only allowed when the source is a container.</summary>
    public string? Compartment { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
    public string? Note { get; init; }
}

public record TrackDurableCommand : ICommand<TrackDurableResponse>
{
    public Guid ConsumableId { get; init; }
    public required TrackDurableRequest Body { get; init; }
}
