namespace Homestead.WebApi.Modules.Inventory.Features.Items;

/// <summary>
/// Read-only tag badge attached to an item projection (durable or consumable). Mirrors the
/// Documents <c>GetDocumentsTagDto</c>: carries the resolved <see cref="EffectiveColor"/> so the
/// client renders inherited colours without a second lookup.
/// </summary>
public record ItemTagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
}
