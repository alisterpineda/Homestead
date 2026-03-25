using Homestead.WebApi.Domain.Parties;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record PartySnapshot : IAuditSnapshot<PartySnapshot, Party>
{
    public required string Name { get; init; }
    public string? Description { get; init; }

    public static PartySnapshot From(Party source) => new()
    {
        Name = source.Name,
        Description = source.Description
    };

    public List<FieldChange> Diff(PartySnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (Description != other.Description)
            changes.Add(new ScalarFieldChange("Description", Description, other.Description));

        return changes;
    }
}
