using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.Login;

public record LoginRequest(string Email, string Password) : IRequest<LoginResponse>;
