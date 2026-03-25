using MediatR;

namespace Homestead.WebApi.Features.Identity.Login;

public record LoginCommand(string Email, string Password) : IRequest<LoginResponse>;
