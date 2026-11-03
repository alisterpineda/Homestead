using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class PdfTextRunnerArgsTests
{
    [Fact]
    public void BuildArguments_LayoutUtf8_WritesToStdout()
    {
        var args = PdfTextRunner.BuildArguments("/data/media/ab/cd.pdf");

        Assert.Contains("-layout", args);
        var enc = args.ToList().IndexOf("-enc");
        Assert.True(enc >= 0 && enc + 1 < args.Count, "Expected '-enc <encoding>'");
        Assert.Equal("UTF-8", args[enc + 1]);
        // pdftotext's positional order is <input> <output>; "-" is stdout.
        Assert.Equal("/data/media/ab/cd.pdf", args[^2]);
        Assert.Equal("-", args[^1]);
    }

    // Poppler ends EVERY page with a form feed, the last included: only that trailing empty segment is
    // dropped, while an interior empty page is a real text-less page that must keep its slot.
    [Theory]
    [InlineData("a\fb\f", new[] { "a", "b" })]
    [InlineData("a\f\fc\f", new[] { "a", "", "c" })]
    [InlineData("a\fb", new[] { "a", "b" })]
    [InlineData("\f", new[] { "" })]
    [InlineData("", new string[0])]
    public void SplitPages_KeepsInteriorBlankPagesAndDropsTheTrailingSegment(string stdout, string[] expected)
    {
        Assert.Equal(expected, PdfTextRunner.SplitPages(stdout));
    }

    // Padding runs shrink to a two-space column break; single spaces inside a cell are untouched.
    [Theory]
    [InlineData("Date        Description            Amount", "Date  Description  Amount")]
    [InlineData("                    Indented", "  Indented")]
    [InlineData("two words  kept", "two words  kept")]
    [InlineData("a\t\t\tb", "a  b")]
    [InlineData("row one      \nrow two   \n", "row one\nrow two\n")]
    [InlineData("", "")]
    public void CompactLayout_CollapsesPaddingToTwoSpacesAndDropsTrailingWhitespace(string page, string expected)
    {
        Assert.Equal(expected, PdfTextRunner.CompactLayout(page));
    }
}
