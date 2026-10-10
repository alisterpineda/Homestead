namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;

/// <summary>
/// Body of a collapse-back-to-a-count: archives the selected individually-tracked items and adds
/// their count to fungible stock at one target location (exactly one of <see cref="AreaId"/> /
/// <see cref="ContainerItemId"/>). Lossy — serials, values, and per-item histories stop being tracked.
/// </summary>
public record CollapseDurablesRequest
{
    /// <summary>The individually-tracked items to fold back into stock.</summary>
    public required IReadOnlyList<Guid> ItemIds { get; init; }

    /// <summary>Target area. Exactly one of this / <see cref="ContainerItemId"/>.</summary>
    public Guid? AreaId { get; init; }

    /// <summary>Target container. Exactly one of this / <see cref="AreaId"/>.</summary>
    public Guid? ContainerItemId { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
    public string? Note { get; init; }
}

public record CollapseDurablesCommand
{
    public Guid ConsumableId { get; init; }
    public required CollapseDurablesRequest Body { get; init; }
}
