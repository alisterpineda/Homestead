using MediatR;

namespace Homestead.WebApi.Modules.Identity.Features.Register;

public record RegisterCommand(string Email, string DisplayName, string Password) : IRequest<RegisterResponse>;
