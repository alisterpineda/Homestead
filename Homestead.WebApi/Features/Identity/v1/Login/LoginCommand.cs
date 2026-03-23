using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.Login;

public record LoginCommand(string Email, string Password) : IRequest<LoginResponse>;
