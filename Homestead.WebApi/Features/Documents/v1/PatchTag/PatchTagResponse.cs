namespace Homestead.WebApi.Features.Documents.v1.PatchTag;

public record PatchTagResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
    public string? Description { get; init; }
}
