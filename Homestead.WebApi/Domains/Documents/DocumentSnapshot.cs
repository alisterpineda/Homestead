using Homestead.WebApi.Domains.Auditing;

namespace Homestead.WebApi.Domains.Documents;

public record DocumentSnapshot : IAuditSnapshot<DocumentSnapshot, Document>
{
    public required string Name { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public required List<string> TagPaths { get; init; }
    public required List<Guid> PartyIds { get; init; }
    public Guid? GroupId { get; init; }

    public static DocumentSnapshot From(Document source) => new()
    {
        Name = source.Name,
        DocumentDate = source.DocumentDate,
        ValidFrom = source.ValidFrom,
        ValidUntil = source.ValidUntil,
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
        if (ValidFrom != other.ValidFrom)
            changes.Add(new ScalarFieldChange("ValidFrom", ValidFrom?.ToString(), other.ValidFrom?.ToString()));
        if (ValidUntil != other.ValidUntil)
            changes.Add(new ScalarFieldChange("ValidUntil", ValidUntil?.ToString(), other.ValidUntil?.ToString()));
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
