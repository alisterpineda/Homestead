namespace Homestead.WebApi.Features.Identity.v1.GetUserInfo;

public record GetUserInfoResponse
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required IList<string> Roles { get; init; }
}
