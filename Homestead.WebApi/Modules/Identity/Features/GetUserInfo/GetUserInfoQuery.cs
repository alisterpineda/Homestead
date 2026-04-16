using MediatR;

namespace Homestead.WebApi.Modules.Identity.Features.GetUserInfo;

public record GetUserInfoQuery(Guid UserId) : IRequest<GetUserInfoResponse?>;
