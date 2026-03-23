using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetParty;

public record GetPartyQuery(Guid Id) : IRequest<GetPartyResponse?>;
