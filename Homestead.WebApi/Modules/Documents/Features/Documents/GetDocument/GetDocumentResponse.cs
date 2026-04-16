namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocument;

public record GetDocumentResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required string FileExtension { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public Guid? UpdatedByUserId { get; init; }
    public string? UpdatedByUserName { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public string Notes { get; init; } = string.Empty;
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string? OwnerUserName { get; init; }
    public Guid? GroupId { get; init; }
    public string? GroupName { get; init; }
    public string? ExtractedText { get; init; }
    public int VersionCount { get; init; }
    public required IReadOnlyList<GetDocumentVersionDto> Versions { get; init; }
    public required IReadOnlyList<GetDocumentTagDto> Tags { get; init; }
    public required IReadOnlyList<GetDocumentPartyDto> Parties { get; init; }
}

public record GetDocumentVersionDto
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

public record GetDocumentTagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
    public string? Description { get; init; }
}

public record GetDocumentPartyDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
