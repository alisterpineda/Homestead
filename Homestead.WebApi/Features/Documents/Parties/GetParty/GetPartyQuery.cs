using MediatR;

namespace Homestead.WebApi.Features.Documents.Parties.GetParty;

public record GetPartyQuery(Guid Id) : IRequest<GetPartyResponse?>;
