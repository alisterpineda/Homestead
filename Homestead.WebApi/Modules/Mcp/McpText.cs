using System.Text;

namespace Homestead.WebApi.Modules.Mcp;

// Head-only chunking of a document's extracted text: a chunk from an offset, the total length, and where the next
// chunk starts (null at the end), so a model reads long text in bounded pieces. The text itself is measured and cut in
// SQL (length() / substr() via McpDocumentReads), so the document is never loaded whole; this class does the
// arithmetic on the counts and snaps a full chunk back to the last whitespace, so no word is split across chunks.
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

    // The chunk as returned, after snapping: when more text follows, the chunk ends before its last whitespace (if
    // it has one past its first code point) and NextOffset points at that whitespace, so the next chunk begins
    // exactly where this one stopped and no character is dropped or repeated. A chunk with no whitespace is kept
    // whole. The last chunk is returned as read, with no next offset. Positions are code points, like the SQL cut.
    public static TextChunkResult Snap(string chunk, int offset, int totalChars)
    {
        var runes = chunk.EnumerateRunes().ToList();
        var end = (long)offset + runes.Count;
        if (end >= totalChars)
            return new TextChunkResult(chunk, null, false);

        var cut = -1;
        var charIndex = 0;
        var cutCharIndex = 0;
        for (var i = 0; i < runes.Count; i++)
        {
            if (i > 0 && Rune.IsWhiteSpace(runes[i]))
            {
                cut = i;
                cutCharIndex = charIndex;
            }

            charIndex += runes[i].Utf16SequenceLength;
        }

        return cut < 0
            ? new TextChunkResult(chunk, (int)end, true)
            : new TextChunkResult(chunk[..cutCharIndex], offset + cut, true);
    }

    // The start to slice from in SQL. EF sends Substring's start as `offset + 1` (SQL substring is 1-based), which
    // overflows int in Postgres at int.MaxValue; capping it changes nothing, since a Postgres value tops out at 1 GB,
    // far below int.MaxValue - 1 code points, so a start that far out reads nothing either way.
    public static int SqlStart(int offset) => Math.Min(offset, int.MaxValue - 1);
}

public readonly record struct TextWindow(int Start, int Length, int? NextOffset);

public readonly record struct TextChunkResult(string Text, int? NextOffset, bool HasMore);
