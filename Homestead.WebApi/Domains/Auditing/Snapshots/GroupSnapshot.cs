using Homestead.WebApi.Domains.Groups;

namespace Homestead.WebApi.Domains.Auditing.Snapshots;

public record GroupSnapshot : IAuditSnapshot<GroupSnapshot, Group>
{
    public required string Name { get; init; }
    public string? Description { get; init; }

    public static GroupSnapshot From(Group source) => new()
    {
        Name = source.Name,
        Description = source.Description
    };

    public List<FieldChange> Diff(GroupSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Description != other.Description)
            changes.Add(new ScalarFieldChange("Description", Description, other.Description));

        return changes;
    }
}
