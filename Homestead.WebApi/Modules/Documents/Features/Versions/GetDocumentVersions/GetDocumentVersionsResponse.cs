namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;

public record GetDocumentVersionsResponse
{
    public required Guid Id { get; init; }
    public required int VersionNumber { get; init; }
    public string? Label { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileExtension { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
