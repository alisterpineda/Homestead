using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpTextTests
{
    [Theory]
    [InlineData(8000, 8000)]
    [InlineData(1000, 1000)]
    [InlineData(999, 1000)]
    [InlineData(0, 1000)]
    [InlineData(50000, 50000)]
    [InlineData(50001, 50000)]
    public void ClampMaxChars_KeepsTheChunkBetween1000And50000(int requested, int expected) =>
        Assert.Equal(expected, McpText.ClampMaxChars(requested));

    // Window works on counts only: the text is sliced in SQL (substr), so offsets are Unicode code points and
    // a chunk can never end inside a character.

    [Fact]
    public void Window_FromTheStart_TakesTheHead_AndSaysWhereToContinue() =>
        Assert.Equal(new TextWindow(0, 4, 4), McpText.Window(10, 0, 4));

    [Fact]
    public void Window_FromAnOffset_TakesTheNextChunk() =>
        Assert.Equal(new TextWindow(4, 4, 8), McpText.Window(10, 4, 4));

    [Fact]
    public void Window_ReachingTheEnd_TakesTheRest_WithNoNextOffset() =>
        Assert.Equal(new TextWindow(8, 2, null), McpText.Window(10, 8, 4));

    [Fact]
    public void Window_EndingExactlyAtTheEnd_HasNoNextOffset() =>
        Assert.Equal(new TextWindow(0, 4, null), McpText.Window(4, 0, 4));

    [Fact]
    public void Window_AtOrPastTheEnd_TakesNothing() =>
        Assert.Equal((new TextWindow(3, 0, null), new TextWindow(5, 0, null)),
            (McpText.Window(3, 3, 4), McpText.Window(3, 5, 4)));

    [Fact]
    public void Window_OfNoText_TakesNothing() =>
        Assert.Equal(new TextWindow(0, 0, null), McpText.Window(0, 0, 4));

    [Fact]
    public void Window_NearIntMaxValue_DoesNotOverflow() =>
        Assert.Equal((new TextWindow(int.MaxValue - 2, 2, null), new TextWindow(int.MaxValue, 0, null)),
            (McpText.Window(int.MaxValue, int.MaxValue - 2, 50_000), McpText.Window(10, int.MaxValue, 50_000)));

    [Fact]
    public void Window_NegativeOffset_IsInvalidInput()
    {
        var e = Assert.Throws<ArgumentException>(() => McpText.Window(3, -1, 4));
        Assert.Contains("text_offset", e.Message);
    }

    // The offset is checked before the text is read (the window is computed after, on the length read with the chunk).
    [Fact]
    public void RequireValidOffset_Negative_IsInvalidInput_ZeroAndAboveAreFine()
    {
        var e = Assert.Throws<ArgumentException>(() => McpText.RequireValidOffset(-1));
        McpText.RequireValidOffset(0);
        McpText.RequireValidOffset(int.MaxValue);
        Assert.Contains("text_offset", e.Message);
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
