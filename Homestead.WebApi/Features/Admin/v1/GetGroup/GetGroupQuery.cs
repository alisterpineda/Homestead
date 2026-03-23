using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.GetGroup;

public record GetGroupQuery(Guid Id) : IRequest<GroupDetailResponse?>;
