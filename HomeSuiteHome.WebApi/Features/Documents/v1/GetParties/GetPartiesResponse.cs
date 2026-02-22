namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetParties;

public record GetPartiesResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required int DocumentCount { get; init; }
}
