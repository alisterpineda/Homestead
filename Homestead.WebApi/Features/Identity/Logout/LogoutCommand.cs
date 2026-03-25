using MediatR;

namespace Homestead.WebApi.Features.Identity.Logout;

public record LogoutCommand : IRequest<LogoutResponse>;
