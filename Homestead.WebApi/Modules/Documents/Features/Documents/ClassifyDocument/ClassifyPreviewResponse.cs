namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyPreviewResponse
{
    public string? SuggestedTitle { get; init; }
    public string? Summary { get; init; }
    public required IReadOnlyList<ClassifyPreviewTagDto> Tags { get; init; }
}

public record ClassifyPreviewTagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
}
