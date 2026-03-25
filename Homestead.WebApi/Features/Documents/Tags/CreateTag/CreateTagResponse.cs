namespace Homestead.WebApi.Features.Documents.Tags.CreateTag;

public record CreateTagResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
    public string? Description { get; init; }
}
