using MediatR;

namespace Homestead.WebApi.Features.Identity.v1.Register;

public record RegisterCommand(string Email, string DisplayName, string Password) : IRequest<RegisterResponse>;
