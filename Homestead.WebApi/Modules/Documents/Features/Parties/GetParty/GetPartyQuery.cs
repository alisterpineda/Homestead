using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetParty;

public record GetPartyQuery(Guid Id) : IRequest<GetPartyResponse?>;
