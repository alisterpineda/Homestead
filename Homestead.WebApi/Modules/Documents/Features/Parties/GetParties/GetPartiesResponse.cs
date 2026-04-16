namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetParties;

public record GetPartiesResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public Guid? UpdatedByUserId { get; init; }
    public string? UpdatedByUserName { get; init; }
    public required int DocumentCount { get; init; }
}
