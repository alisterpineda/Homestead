using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public record GetPartiesByIdsQuery(IReadOnlyList<Guid> Ids)
    : IRequest<IReadOnlyList<GetPartiesByIdsResponse>>;
