namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetParty;

public record GetPartyResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<GetPartyDocumentDto> Documents { get; init; }
}

public record GetPartyDocumentDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
}
