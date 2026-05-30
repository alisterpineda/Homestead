using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.CreateParty;

public record CreatePartyCommand : ICommand<CreatePartyResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
