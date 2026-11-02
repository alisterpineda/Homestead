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

    [Fact]
    public void Slice_FromTheStart_ReturnsTheHead_AndWhereToContinue() =>
        Assert.Equal(new TextChunk("abcd", 10, 4), McpText.Slice("abcdefghij", 0, 4));

    [Fact]
    public void Slice_FromAnOffset_ReturnsTheNextChunk() =>
        Assert.Equal(new TextChunk("efgh", 10, 8), McpText.Slice("abcdefghij", 4, 4));

    [Fact]
    public void Slice_ReachingTheEnd_HasNoNextOffset() =>
        Assert.Equal(new TextChunk("ij", 10, null), McpText.Slice("abcdefghij", 8, 4));

    [Fact]
    public void Slice_EndingExactlyAtTheEnd_HasNoNextOffset() =>
        Assert.Equal(new TextChunk("abcd", 4, null), McpText.Slice("abcd", 0, 4));

    [Fact]
    public void Slice_PastTheEnd_IsEmpty_AndStillReportsTheTotal() =>
        Assert.Equal(new TextChunk("", 3, null), McpText.Slice("abc", 5, 4));

    [Fact]
    public void Slice_OfNoText_IsEmpty_WithZeroTotal() =>
        Assert.Equal(new TextChunk("", 0, null), McpText.Slice(null, 0, 4));

    [Fact]
    public void Slice_NeverSplitsASurrogatePair()
    {
        // "😀" is two UTF-16 chars at indexes 2 and 3; a 3-char chunk would end between them.
        Assert.Equal(new TextChunk("ab", 6, 2), McpText.Slice("ab😀cd", 0, 3));
    }

    [Fact]
    public void Slice_NegativeOffset_IsInvalidInput()
    {
        var e = Assert.Throws<ArgumentException>(() => McpText.Slice("abc", -1, 4));
        Assert.Contains("text_offset", e.Message);
    }
}
