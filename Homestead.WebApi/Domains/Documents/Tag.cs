using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Domains.Documents;

public class Tag
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public Guid? ParentId { get; set; }
    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Tag? Parent { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<Tag> Children { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
}
