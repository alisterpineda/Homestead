namespace Homestead.WebApi.Features.Documents.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

public record TextExtractionEngineSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
