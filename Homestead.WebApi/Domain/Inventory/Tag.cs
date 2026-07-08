using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Inventory;

public class Tag
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public Guid? ParentId { get; set; }
    public string? Color { get; set; }
    public string? EffectiveColor { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public Tag? Parent { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
    public ICollection<Tag> Children { get; set; } = [];
    public ICollection<ItemTag> ItemTags { get; set; } = [];
}
