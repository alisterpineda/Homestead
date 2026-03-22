using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.CreateParty;

public record CreatePartyRequest : IRequest<CreatePartyResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
