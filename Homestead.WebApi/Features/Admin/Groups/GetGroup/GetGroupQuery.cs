using MediatR;

namespace Homestead.WebApi.Features.Admin.Groups.GetGroup;

public record GetGroupQuery(Guid Id) : IRequest<GroupDetailResponse?>;
