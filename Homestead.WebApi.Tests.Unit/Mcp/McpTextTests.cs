using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpTextTests
{
    // Snap: a full chunk with more text behind it ends before its last whitespace, and the next offset points at that
    // whitespace, so the chunks concatenate back to the original text.

    [Fact]
    public void Snap_FullChunkWithMoreBehind_EndsBeforeItsLastWhitespace_AndContinuesThere()
    {
        // 10 code points of a 25-code-point text: "alpha beta" → "alpha", next at the space (5).
        var result = McpText.Snap("alpha beta", offset: 0, totalChars: 25);

        Assert.Equal(new TextChunkResult("alpha", 5, true), result);
    }

    [Fact]
    public void Snap_FromAnOffset_CountsTheNextOffsetFromTheStartOfTheText()
    {
        var result = McpText.Snap(" beta gamm", offset: 5, totalChars: 25);

        // The chunk's own leading space is not a cut point (index 0); the cut is at the space before "gamm".
        Assert.Equal(new TextChunkResult(" beta", 10, true), result);
    }

    [Fact]
    public void Snap_ChunkWithoutWhitespace_IsKeptWhole()
    {
        Assert.Equal(new TextChunkResult("abcdefghij", 10, true), McpText.Snap("abcdefghij", 0, 25));
    }

    [Fact]
    public void Snap_LastChunk_IsReturnedAsRead_WithNoNextOffset()
    {
        Assert.Equal(new TextChunkResult("ta end", null, false), McpText.Snap("ta end", 19, 25));
    }

    [Fact]
    public void Snap_CountsCodePoints_NotUtf16Units()
    {
        // 🙂 is one code point (two UTF-16 units): the next offset is 4 (code points), the text cut by chars.
        var result = McpText.Snap("ab🙂d ef", offset: 0, totalChars: 20);

        Assert.Equal(new TextChunkResult("ab🙂d", 4, true), result);
    }

    [Fact]
    public void Snap_OnlyWhitespaceAtTheStart_IsKeptWhole()
    {
        Assert.Equal(new TextChunkResult(" abcdef", 7, true), McpText.Snap(" abcdef", 0, 20));
    }

    // SQL's substring is 1-based, so the start is sent as offset + 1 and int.MaxValue would overflow in Postgres.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 1000)]
    [InlineData(int.MaxValue - 1, int.MaxValue - 1)]
    [InlineData(int.MaxValue, int.MaxValue - 1)]
    public void SqlStart_CapsTheOffset_SoTheOneBasedStartCannotOverflow(int offset, int expected) =>
        Assert.Equal(expected, McpText.SqlStart(offset));
}
