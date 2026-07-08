using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

public record GetInventoryTagsByIdsQuery(IReadOnlyList<Guid> Ids)
    : IRequest<IReadOnlyList<GetInventoryTagsByIdsResponse>>;
