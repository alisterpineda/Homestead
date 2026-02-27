namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentVersions;

public record GetDocumentVersionsResponse
{
    public required Guid Id { get; init; }
    public required int VersionNumber { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileExtension { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
