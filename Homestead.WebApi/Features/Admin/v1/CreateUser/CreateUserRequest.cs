using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Features.Admin.v1.CreateUser;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Password { get; init; }
    public AppRole Role { get; init; } = AppRole.Contributor;
}
