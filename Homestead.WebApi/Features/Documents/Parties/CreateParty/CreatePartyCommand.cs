using MediatR;

namespace Homestead.WebApi.Features.Documents.Parties.CreateParty;

public record CreatePartyCommand : IRequest<CreatePartyResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
