namespace Homestead.WebApi.Modules.Mcp;

// Head-only chunking of a document's extracted text: a chunk from an offset, the total length, and where the next
// chunk starts (null at the end), so a model reads long text in bounded pieces.
public static class McpText
{
    public const int DefaultMaxChars = 8000;
    public const int MinMaxChars = 1000;
    public const int MaxMaxChars = 50_000;

    public static int ClampMaxChars(int maxChars) => Math.Clamp(maxChars, MinMaxChars, MaxMaxChars);

    // Offsets count UTF-16 chars. A chunk never ends between the halves of a surrogate pair (a lone surrogate is not
    // valid text on the wire); the next chunk starts where this one stopped.
    public static TextChunk Slice(string? text, int offset, int maxChars)
    {
        if (offset < 0)
            throw new ArgumentException("text_offset must be 0 or greater.");

        text ??= "";
        if (offset >= text.Length)
            return new TextChunk("", text.Length, null);

        var end = Math.Min(text.Length, offset + maxChars);
        if (end < text.Length && end - offset > 1 && char.IsHighSurrogate(text[end - 1]))
            end--;

        return new TextChunk(text[offset..end], text.Length, end < text.Length ? end : null);
    }
}

public readonly record struct TextChunk(string Text, int TotalChars, int? NextOffset);
