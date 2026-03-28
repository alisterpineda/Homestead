using Homestead.WebApi.Domain.Groups;

namespace Homestead.WebApi.Features.Admin.Groups;

public record GroupResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Guid? UpdatedByUserId { get; init; }
    public string? UpdatedByUserName { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public int MemberCount { get; init; }
}

public record GroupDetailResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public required IReadOnlyList<GroupMemberResponse> Members { get; init; }
}

public record GroupMemberResponse
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public required GroupAccessLevel AccessLevel { get; init; }
}
