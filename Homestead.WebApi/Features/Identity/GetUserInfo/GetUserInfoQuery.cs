using MediatR;

namespace Homestead.WebApi.Features.Identity.GetUserInfo;

public record GetUserInfoQuery(Guid UserId) : IRequest<GetUserInfoResponse?>;
