namespace Homestead.WebApi.Features.Admin.v1.GetUsers;

public record GetUsersResponse
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
