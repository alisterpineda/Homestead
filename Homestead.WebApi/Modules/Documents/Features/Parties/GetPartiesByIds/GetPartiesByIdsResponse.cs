namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public record GetPartiesByIdsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
