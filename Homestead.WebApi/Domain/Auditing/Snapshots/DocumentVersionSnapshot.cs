using System.Security.Cryptography;
using System.Text;
using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Domain.Auditing.Snapshots;

public record DocumentVersionSnapshot : IAuditSnapshot<DocumentVersionSnapshot, DocumentVersion>
{
    // The longest extracted text an audit row records verbatim. Beyond this the diff records a
    // prefix + digest instead (see DescribeExtractedText).
    private const int ExtractedTextAuditLimit = 512;

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
            changes.Add(new ScalarFieldChange("ExtractedText",
                DescribeExtractedText(ExtractedText), DescribeExtractedText(other.ExtractedText)));

        return changes;
    }

    // Long extracted text is summarized rather than stored verbatim, twice, per audit row.
    //
    // This diff used to carry the entire old AND new text. That was survivable only because switching
    // engines was impossible: a reprocess re-ran the same engine over the same bytes, produced identical
    // text, and the diff came out empty. Per-run engine selection inverts that — "reprocess 500 documents
    // with azure-layout" makes every diff non-empty by construction, at roughly 100 KB per 50-page
    // statement, so ~10 MB of audit JSONB for a single bulk run.
    //
    // The trade is deliberate and it does change audit semantics: the log still records THAT the text
    // changed, and the digest still proves which text a row refers to, but the audit table is no longer a
    // store of document text. Recovering previous text was never this table's job, and a 100 KB
    // before/after pair was never a readable diff in the first place.
    private static string? DescribeExtractedText(string? text)
    {
        if (text is null)
            return null;
        if (text.Length <= ExtractedTextAuditLimit)
            return text;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        return $"{text[..ExtractedTextAuditLimit]}… [{text.Length:N0} chars, sha256:{hash}]";
    }
}
