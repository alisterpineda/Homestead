using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Modules.Admin.Features.Users.CreateUser;

public record CreateUserCommand : ICommand<UserResponse>
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public AppRole Role { get; init; } = AppRole.Contributor;
}
