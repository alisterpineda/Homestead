namespace Homestead.WebApi.Modules.Inventory.Features.Tags.PatchTag;

public record PatchInventoryTagResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Guid? UpdatedByUserId { get; init; }
    public string? UpdatedByUserName { get; init; }
}
