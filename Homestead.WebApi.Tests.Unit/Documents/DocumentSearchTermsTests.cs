using System.Linq.Expressions;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class DocumentSearchTermsTests
{
    [Theory]
    [InlineData("car repair", @"(?=.*\mcar\M)(?=.*\mrepair)")]
    [InlineData("Bucharest", @"(?=.*\mBucharest)")]
    [InlineData("\"car wash\" 2024", @"(?=.*\mcar\M)(?=.*\mwash)(?=.*\m2024)")]
    [InlineData("a.b (c)", @"(?=.*\ma\.b\M)(?=.*\mc\M)")]
    [InlineData("car CAR", @"(?=.*\mcar\M)")]
    public void WordPattern_RequiresEveryTermAsAWord_PrefixingOnlyLongOnes(string search, string expected)
    {
        Assert.Equal(expected, DocumentSearchTerms.WordPattern(search, DocumentSearchTerms.WordsPrefixMinLength));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("... ,,,")]
    public void WordPattern_NothingLeft_IsNull(string search)
    {
        Assert.Null(DocumentSearchTerms.WordPattern(search, DocumentSearchTerms.WordsPrefixMinLength));
    }

    [Fact]
    public void WordPattern_EscapesRegexMetacharacters()
    {
        Assert.Equal(@"(?=.*\m\#702-1234567)", DocumentSearchTerms.WordPattern("#702-1234567", 4));
    }

    [Fact]
    public void Build_Words_ExposesTheBranches_AndATsQueryWithExactShortTerms()
    {
        var terms = DocumentSearchTerms.Build("car repair", DocumentMatchMode.Words);

        Assert.Equal((DocumentMatchMode.Words, "car & repair:*"), (terms.Mode, terms.TsQuery));
        Assert.All(new[] { terms.Name, terms.Content, terms.Tag, terms.Party, terms.Notes, terms.Any, terms.Direct },
            Assert.NotNull);
    }

    [Fact]
    public void Build_Substring_PrefixesEveryTerm()
    {
        var terms = DocumentSearchTerms.Build("car repair", DocumentMatchMode.Substring);

        Assert.Equal((DocumentMatchMode.Substring, "car:* & repair:*"), (terms.Mode, terms.TsQuery));
    }

    [Fact]
    public void PredicateBuilder_Or_CombinesOntoOneParameter()
    {
        Expression<Func<int, bool>> even = n => n % 2 == 0;
        Expression<Func<int, bool>> big = m => m > 10;

        var any = PredicateBuilder.Or(even, big).Compile();

        Assert.Equal((true, true, false), (any(4), any(11), any(3)));
        Assert.False(PredicateBuilder.Or<int>().Compile()(1));
    }
}
