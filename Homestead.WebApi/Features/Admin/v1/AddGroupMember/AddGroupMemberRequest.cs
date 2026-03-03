using Homestead.WebApi.Domains.Groups;

namespace Homestead.WebApi.Features.Admin.v1.AddGroupMember;

public record AddGroupMemberRequest
{
    public required Guid UserId { get; init; }
    public required GroupAccessLevel AccessLevel { get; init; }
}
