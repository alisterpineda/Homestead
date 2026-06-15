namespace Homestead.WebApi.Modules.Inventory.Features.Durables;

/// <summary>What a single <see cref="LocationSegmentResponse"/> represents in a resolved path.</summary>
public enum LocationSegmentKind
{
    /// <summary>A structural area (the root of a resolved chain when the chain reaches one).</summary>
    Area,

    /// <summary>A container item somewhere along the chain.</summary>
    Container,
}

/// <summary>
/// One step in an item's resolved location path, root→immediate-parent. The serialized enum is a
/// string (global converter), so the frontend sees a discriminated <c>'Area' | 'Container'</c> union.
/// </summary>
public record LocationSegmentResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required LocationSegmentKind Kind { get; init; }
}
