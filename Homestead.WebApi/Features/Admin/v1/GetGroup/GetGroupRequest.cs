using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.GetGroup;

public record GetGroupRequest(Guid Id) : IRequest<GroupDetailResponse?>;
