using Homestead.WebApi.Domain.Groups;

namespace Homestead.WebApi.Features.Admin.Groups.UpdateGroupMember;

public record UpdateGroupMemberRequest
{
    public required GroupAccessLevel AccessLevel { get; init; }
}
