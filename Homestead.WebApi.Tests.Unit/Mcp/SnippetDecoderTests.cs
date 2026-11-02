using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class SnippetDecoderTests
{
    [Fact]
    public void Decode_StripsTheHighlightMarks_AndDecodesEntities() =>
        Assert.Equal("Paid & settled by \"Acme\" <ref>",
            SnippetDecoder.Decode("Paid &amp; <mark>settled</mark> by &quot;Acme&quot; &lt;ref&gt;"));

    [Fact]
    public void Decode_DecodesNumericEntities() =>
        Assert.Equal("Bob's bill", SnippetDecoder.Decode("Bob&#39;s <mark>bill</mark>"));

    [Fact]
    public void Decode_KeepsMarkupThatWasLiteralDocumentText()
    {
        // The document itself said "<mark>"; encoded, it is not a highlight and must survive decoding.
        Assert.Equal("<mark>literal", SnippetDecoder.Decode("&lt;mark&gt;<mark>literal</mark>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Decode_NoSnippet_IsNull(string? snippet) =>
        Assert.Null(SnippetDecoder.Decode(snippet));
}
