namespace Homestead.WebApi.Features.Admin.v1.UpdateUser;

public record UpdateUserRequest
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public string? Password { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsActive { get; init; }
}

public record UpdateUserCommand
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public string? Password { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsActive { get; init; }
}
