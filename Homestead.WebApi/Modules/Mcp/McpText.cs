namespace Homestead.WebApi.Modules.Mcp;

// Head-only chunking of a document's extracted text: a chunk from an offset, the total length, and where the next
// chunk starts (null at the end), so a model reads long text in bounded pieces. The text itself is measured and cut in
// SQL (length() / substr() via McpDocumentReads), so the document is never loaded whole; this class only does the
// arithmetic on the counts.
public static class McpText
{
    public const int DefaultMaxChars = 8000;
    public const int MinMaxChars = 1000;
    public const int MaxMaxChars = 50_000;

    // Offsets and lengths count Unicode code points — what Postgres length()/substr() count on a UTF-8 database — so
    // a chunk never ends inside a character, and feeding next_offset back reads on with nothing dropped or repeated.
    // At or past the end the window is empty (Length 0) with no next offset. The offset is clamped to 0 or more and
    // maxChars to [MinMaxChars, MaxMaxChars] by the tool (McpWarnings) before anything is read.
    public static TextWindow Window(int totalChars, int offset, int maxChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset >= totalChars)
            return new TextWindow(offset, 0, null);

        var end = (int)Math.Min(totalChars, (long)offset + maxChars);
        return new TextWindow(offset, end - offset, end < totalChars ? end : null);
    }

    // The start to slice from in SQL. EF sends Substring's start as `offset + 1` (SQL substring is 1-based), which
    // overflows int in Postgres at int.MaxValue; capping it changes nothing, since a Postgres value tops out at 1 GB,
    // far below int.MaxValue - 1 code points, so a start that far out reads nothing either way.
    public static int SqlStart(int offset) => Math.Min(offset, int.MaxValue - 1);
}

public readonly record struct TextWindow(int Start, int Length, int? NextOffset);
