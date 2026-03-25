using MediatR;

namespace Homestead.WebApi.Features.Identity.Register;

public record RegisterCommand(string Email, string DisplayName, string Password) : IRequest<RegisterResponse>;
