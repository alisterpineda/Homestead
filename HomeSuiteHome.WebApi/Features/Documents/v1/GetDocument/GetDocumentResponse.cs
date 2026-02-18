namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocument;

public record GetDocumentResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
