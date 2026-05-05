using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class SearchTsQueryBuilderTests
{
    [Fact]
    public void Build_UnquotedTerms_EmitsPrefixAndClauses()
    {
        Assert.Equal("tax:* & invoice:*", SearchTsQueryBuilder.Build("tax invoice"));
    }

    [Fact]
    public void Build_QuotedPhrase_EmitsFollowedByClause()
    {
        Assert.Equal("(operating <-> agreement)", SearchTsQueryBuilder.Build("\"operating agreement\""));
    }

    [Fact]
    public void Build_MixedPhraseAndPrefix_EmitsPhraseFirstThenPrefixes()
    {
        Assert.Equal(
            "(operating <-> agreement) & tax:* & 2024:*",
            SearchTsQueryBuilder.Build("tax \"operating agreement\" 2024"));
    }

    [Fact]
    public void Build_EmptyQuotes_ReturnsNull()
    {
        Assert.Null(SearchTsQueryBuilder.Build("\"\""));
    }

    [Fact]
    public void Build_QuotedStopwordOnly_ReturnsNull()
    {
        Assert.Null(SearchTsQueryBuilder.Build("\"the\""));
    }

    [Fact]
    public void Build_QuotedWhitespaceOnly_ReturnsNull()
    {
        Assert.Null(SearchTsQueryBuilder.Build("\"   \""));
    }

    [Fact]
    public void Build_PhraseWithSpecialChars_StripsAndEmitsFollowedBy()
    {
        Assert.Equal("(foo <-> bar)", SearchTsQueryBuilder.Build("\"foo & bar\""));
    }

    [Fact]
    public void Build_PhraseWithSpecialCharsNoWhitespace_CollapsesToSingleToken()
    {
        Assert.Equal("foobar", SearchTsQueryBuilder.Build("\"foo&&bar\""));
    }

    [Fact]
    public void Build_UnmatchedQuote_StripsAndTreatsAsPrefixes()
    {
        Assert.Equal("unmatched:* & quote:*", SearchTsQueryBuilder.Build("unmatched\"quote"));
    }

    [Fact]
    public void Build_OnlyQuotes_ReturnsNull()
    {
        Assert.Null(SearchTsQueryBuilder.Build("\"\"\"\""));
    }

    [Fact]
    public void Build_TwoPhrases_EmitsBothFollowedByClauses()
    {
        Assert.Equal(
            "(foo <-> bar) & (baz <-> qux)",
            SearchTsQueryBuilder.Build("\"foo bar\" \"baz qux\""));
    }
}
