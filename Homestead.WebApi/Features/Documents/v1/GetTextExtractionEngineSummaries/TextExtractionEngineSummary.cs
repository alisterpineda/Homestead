namespace Homestead.WebApi.Features.Documents.v1.GetTextExtractionEngineSummaries;

public record TextExtractionEngineSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
