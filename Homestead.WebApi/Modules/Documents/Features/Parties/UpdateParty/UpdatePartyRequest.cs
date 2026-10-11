namespace Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;

public record UpdatePartyRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
