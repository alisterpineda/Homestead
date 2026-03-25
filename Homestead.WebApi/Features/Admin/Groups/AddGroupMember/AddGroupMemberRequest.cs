using Homestead.WebApi.Domain.Groups;

namespace Homestead.WebApi.Features.Admin.Groups.AddGroupMember;

public record AddGroupMemberRequest
{
    public required Guid UserId { get; init; }
    public required GroupAccessLevel AccessLevel { get; init; }
}
