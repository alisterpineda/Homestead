using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Features.Identity.GetUserInfo;

public record GetUserInfoResponse
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required AppRole Role { get; init; }
    public required bool IsActive { get; init; }
}
