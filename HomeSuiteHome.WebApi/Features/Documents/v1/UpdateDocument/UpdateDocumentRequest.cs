namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;

public record UpdateDocumentRequest
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public List<string> TagPaths { get; init; } = [];
}
