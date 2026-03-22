using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetParty;

public record GetPartyRequest(Guid Id) : IRequest<GetPartyResponse?>;
