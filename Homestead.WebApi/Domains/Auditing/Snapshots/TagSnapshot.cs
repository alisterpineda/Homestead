using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Domains.Auditing.Snapshots;

public record TagSnapshot : IAuditSnapshot<TagSnapshot, Tag>
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
    public string? Color { get; init; }

    public static TagSnapshot From(Tag source) => new()
    {
        Name = source.Name,
        Path = source.Path,
        ParentId = source.ParentId,
        Color = source.Color
    };

    public List<FieldChange> Diff(TagSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Path != other.Path)
            changes.Add(new ScalarFieldChange("Path", Path, other.Path));
        if (ParentId != other.ParentId)
            changes.Add(new ScalarFieldChange("ParentId", ParentId?.ToString(), other.ParentId?.ToString()));
        if (Color != other.Color)
            changes.Add(new ScalarFieldChange("Color", Color, other.Color));

        return changes;
    }
}
