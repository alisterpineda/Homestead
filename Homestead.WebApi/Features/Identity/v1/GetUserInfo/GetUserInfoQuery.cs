using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.GetUserInfo;

public record GetUserInfoQuery(Guid UserId) : IRequest<GetUserInfoResponse?>;
