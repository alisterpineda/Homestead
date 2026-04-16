using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.CreateParty;

public record CreatePartyCommand : IRequest<CreatePartyResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
