using Homestead.WebApi.Domains.Identity;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.CreateUser;

public record CreateUserCommand : IRequest<UserResponse>
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Password { get; init; }
    public AppRole Role { get; init; } = AppRole.Contributor;
}
