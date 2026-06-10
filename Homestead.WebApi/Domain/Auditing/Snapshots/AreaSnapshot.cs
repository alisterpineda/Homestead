using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record AreaSnapshot : IAuditSnapshot<AreaSnapshot, Area>
{
    public required string Name { get; init; }

    public static AreaSnapshot From(Area source) => new()
    {
        Name = source.Name,
    };

    public List<FieldChange> Diff(AreaSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));

        return changes;
    }
}
