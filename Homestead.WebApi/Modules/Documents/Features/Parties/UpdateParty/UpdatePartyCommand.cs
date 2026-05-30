using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;

public record UpdatePartyRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public record UpdatePartyCommand : ICommand<UpdatePartyResponse?>
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
