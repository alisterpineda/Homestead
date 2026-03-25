namespace Homestead.WebApi.Features.Documents.Parties.GetParty;

public record GetPartyResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
