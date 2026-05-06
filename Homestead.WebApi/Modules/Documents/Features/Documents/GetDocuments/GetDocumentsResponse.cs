namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public record GetDocumentsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileExtension { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string? OwnerUserName { get; init; }
    public Guid? GroupId { get; init; }
    public string? GroupName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public Guid? UpdatedByUserId { get; init; }
    public string? UpdatedByUserName { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public Guid? DeletedByUserId { get; init; }
    public string? DeletedByUserName { get; init; }
    public int VersionCount { get; init; }
    public bool HasThumbnail { get; init; }
    public string? MatchSnippet { get; init; }
    public required IReadOnlyList<GetDocumentsTagDto> Tags { get; init; }
    public required IReadOnlyList<GetDocumentsPartyDto> Parties { get; init; }
}

public record GetDocumentsTagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
}

public record GetDocumentsPartyDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
