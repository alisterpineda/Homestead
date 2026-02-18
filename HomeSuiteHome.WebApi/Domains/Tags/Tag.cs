namespace HomeSuiteHome.WebApi.Domains.Tags;

public class Tag
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public Guid? ParentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tag? Parent { get; set; }
    public ICollection<Tag> Children { get; set; } = [];
    public ICollection<DocumentTag> DocumentTags { get; set; } = [];
}
