using Homestead.WebApi.Domains.Groups;

namespace Homestead.WebApi.Features.Admin.v1.UpdateGroupMember;

public record UpdateGroupMemberRequest
{
    public required GroupAccessLevel AccessLevel { get; init; }
}
