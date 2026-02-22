namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocuments;

public record GetDocumentsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required IReadOnlyList<GetDocumentsTagDto> Tags { get; init; }
    public required IReadOnlyList<GetDocumentsPartyDto> Parties { get; init; }
}

public record GetDocumentsTagDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
}

public record GetDocumentsPartyDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
