using MediatR;

namespace Homestead.WebApi.Modules.Identity.Features.Logout;

public record LogoutCommand : IRequest<LogoutResponse>;
