using Homestead.WebApi.Common.Search;

namespace Homestead.WebApi.Tests.Unit.Search;

public class LikeEscaperTests
{
    [Theory]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("100%_off", "100\\%\\_off")]
    public void Escape_WildcardMetacharacters_AreBackslashEscaped(string input, string expected)
    {
        Assert.Equal(expected, LikeEscaper.Escape(input));
    }

    [Fact]
    public void Escape_Backslash_IsEscapedFirst()
    {
        // Backslash must be doubled before the %/_ escapes are introduced, or their new backslashes
        // would be re-escaped into "\\%". A literal "\%" input becomes "\\\%": doubled slash + escaped %.
        Assert.Equal("\\\\\\%", LikeEscaper.Escape("\\%"));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("no wildcards here 123")]
    [InlineData("")]
    public void Escape_NoMetacharacters_ReturnsInputUnchanged(string input)
    {
        Assert.Equal(input, LikeEscaper.Escape(input));
    }
}
