namespace Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;

/// <summary>
/// Scope for a place drill-down: exactly one of <see cref="AreaId"/>, <see cref="ContainerItemId"/>,
/// or <see cref="Unplaced"/> must be set.
/// </summary>
public record GetPlaceContentsQuery
{
    /// <summary>Browse the direct contents of this area.</summary>
    public Guid? AreaId { get; init; }

    /// <summary>Browse the direct contents of this container item.</summary>
    public Guid? ContainerItemId { get; init; }

    /// <summary>Browse items with no direct location (neither area nor container).</summary>
    public bool? Unplaced { get; init; }
}
