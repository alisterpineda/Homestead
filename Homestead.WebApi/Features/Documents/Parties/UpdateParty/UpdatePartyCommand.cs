using MediatR;

namespace Homestead.WebApi.Features.Documents.Parties.UpdateParty;

public record UpdatePartyRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public record UpdatePartyCommand : IRequest<UpdatePartyResponse?>
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
