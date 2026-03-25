using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record DocumentVersionSnapshot : IAuditSnapshot<DocumentVersionSnapshot, DocumentVersion>
{
    public string? Label { get; init; }
    public string? ExtractedText { get; init; }

    public static DocumentVersionSnapshot From(DocumentVersion source) => new()
    {
        Label = source.Label,
        ExtractedText = source.ExtractedText
    };

    public List<FieldChange> Diff(DocumentVersionSnapshot other)
    {
        var changes = new List<FieldChange>();

        if (Label != other.Label)
            changes.Add(new ScalarFieldChange("Label", Label, other.Label));
        if (ExtractedText != other.ExtractedText)
            changes.Add(new ScalarFieldChange("ExtractedText", ExtractedText, other.ExtractedText));

        return changes;
    }
}
