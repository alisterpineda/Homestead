using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Groups;

public class Group
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }

    public ICollection<GroupMember> Members { get; set; } = [];
}
