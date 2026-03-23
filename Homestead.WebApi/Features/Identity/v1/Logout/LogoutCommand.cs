using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.Logout;

public record LogoutCommand : IRequest<LogoutResponse>;
