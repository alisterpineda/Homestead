namespace Homestead.WebApi.Features.Admin.v1;

public record UserResponse
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public required bool IsAdmin { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
