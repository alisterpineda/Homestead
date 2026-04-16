using Homestead.WebApi.Domain.Identity;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Users.UpdateUser;

public record UpdateUserRequest
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public AppRole Role { get; init; }
    public bool IsActive { get; init; }
}

public record UpdateUserCommand : IRequest<UserResponse?>
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public AppRole Role { get; init; }
    public bool IsActive { get; init; }
}
