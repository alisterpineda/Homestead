namespace Homestead.WebApi.Features.Documents.Documents.PatchDocument;

public record PatchDocumentResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required string FileExtension { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
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
    public required IReadOnlyList<TagDto> Tags { get; init; }
    public required IReadOnlyList<PartyDto> Parties { get; init; }
}

public record TagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Color { get; init; }
    public string? EffectiveColor { get; init; }
}

public record PartyDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
