namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocuments;

public record GetDocumentsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
}
