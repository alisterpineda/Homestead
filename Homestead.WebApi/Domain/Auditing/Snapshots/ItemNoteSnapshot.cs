using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record ItemNoteSnapshot : IAuditSnapshot<ItemNoteSnapshot, ItemNote>
{
    public required DateTimeOffset OccurredAt { get; init; }
    public string? Subject { get; init; }
    public required string Body { get; init; }

    public static ItemNoteSnapshot From(ItemNote source) => new()
    {
        OccurredAt = source.OccurredAt,
        Subject = source.Subject,
        Body = source.Body,
    };

    public List<FieldChange> Diff(ItemNoteSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (OccurredAt != other.OccurredAt)
            changes.Add(new ScalarFieldChange("OccurredAt", OccurredAt.ToString("O"), other.OccurredAt.ToString("O")));

        if (Subject != other.Subject)
            changes.Add(new ScalarFieldChange("Subject", Subject, other.Subject));

        if (Body != other.Body)
            changes.Add(new ScalarFieldChange("Body", Body, other.Body));

        return changes;
    }
}
