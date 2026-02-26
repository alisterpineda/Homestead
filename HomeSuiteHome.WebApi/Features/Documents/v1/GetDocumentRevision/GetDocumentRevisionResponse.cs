namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentRevision;

public record GetDocumentRevisionResponse
{
    public required Guid Id { get; init; }
    public required int RevisionNumber { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required string FileExtension { get; init; }
    public string? ExtractedText { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
