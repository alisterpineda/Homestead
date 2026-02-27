namespace Homestead.WebApi.Features.Documents.v1.CreateParty;

public record CreatePartyResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
