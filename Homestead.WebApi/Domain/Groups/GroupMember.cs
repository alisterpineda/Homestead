using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Groups;

public class GroupMember
{
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public GroupAccessLevel AccessLevel { get; set; }
}
