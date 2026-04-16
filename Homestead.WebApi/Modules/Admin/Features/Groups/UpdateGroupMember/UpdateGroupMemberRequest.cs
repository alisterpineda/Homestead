using Homestead.WebApi.Domain.Groups;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;

public record UpdateGroupMemberRequest
{
    public required GroupAccessLevel AccessLevel { get; init; }
}
