using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record DocumentSnapshot : IAuditSnapshot<DocumentSnapshot, Document>
{
    public required string Name { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public string Notes { get; init; } = string.Empty;
    public required List<string> TagPaths { get; init; }
    public required List<Guid> PartyIds { get; init; }
    public Guid? GroupId { get; init; }

    public static DocumentSnapshot From(Document source) => new()
    {
        Name = source.Name,
        DocumentDate = source.DocumentDate,
        ExpiryDate = source.ExpiryDate,
        Notes = source.Notes,
        TagPaths = source.DocumentTags.Select(dt => dt.Tag.Path).Order().ToList(),
        PartyIds = source.DocumentParties.Select(dp => dp.PartyId).Order().ToList(),
        GroupId = source.GroupId
    };

    public List<FieldChange> Diff(DocumentSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Name != other.Name)
            changes.Add(new ScalarFieldChange("Name", Name, other.Name));
        if (DocumentDate != other.DocumentDate)
            changes.Add(new ScalarFieldChange("DocumentDate", DocumentDate?.ToString(), other.DocumentDate?.ToString()));
        if (ExpiryDate != other.ExpiryDate)
            changes.Add(new ScalarFieldChange("ExpiryDate", ExpiryDate?.ToString(), other.ExpiryDate?.ToString()));
        if (Notes != other.Notes)
            changes.Add(new ScalarFieldChange("Notes", Notes, other.Notes));
        if (!TagPaths.SequenceEqual(other.TagPaths))
            changes.Add(new ArrayFieldChange("TagPaths", TagPaths, other.TagPaths));
        if (!PartyIds.SequenceEqual(other.PartyIds))
            changes.Add(new ArrayFieldChange("PartyIds",
                PartyIds.Select(p => p.ToString()).ToList(),
                other.PartyIds.Select(p => p.ToString()).ToList()));
        if (GroupId != other.GroupId)
            changes.Add(new ScalarFieldChange("GroupId", GroupId?.ToString(), other.GroupId?.ToString()));

        return changes;
    }
}
