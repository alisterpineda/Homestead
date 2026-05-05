using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public record GetTagsByIdsQuery(IReadOnlyList<Guid> Ids)
    : IRequest<IReadOnlyList<GetTagsByIdsResponse>>;
