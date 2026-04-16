using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.GetGroup;

public record GetGroupQuery(Guid Id) : IRequest<GroupDetailResponse?>;
