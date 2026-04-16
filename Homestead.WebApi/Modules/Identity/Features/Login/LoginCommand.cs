using MediatR;

namespace Homestead.WebApi.Modules.Identity.Features.Login;

public record LoginCommand(string Email, string Password) : IRequest<LoginResponse>;
