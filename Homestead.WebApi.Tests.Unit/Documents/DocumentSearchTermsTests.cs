using System.Linq.Expressions;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class DocumentSearchTermsTests
{
    [Theory]
    [InlineData("car repair", @"\mcar\M|\mrepair")]
    [InlineData("Bucharest", @"\mBucharest")]
    [InlineData("\"car wash\" 2024", @"\mcar\M|\mwash|\m2024")]
    [InlineData("a.b (c)", @"\ma\.b\M|\mc\M")]
    [InlineData("car CAR", @"\mcar\M")]
    [InlineData("renewed\tcancelled", @"\mrenewed|\mcancelled")]
    public void WordPatterns_OnePerTerm_PrefixingOnlyLongOnes(string search, string expected)
    {
        Assert.Equal(expected, string.Join("|", DocumentSearchTerms.WordPatterns(search, DocumentSearchTerms.WordsPrefixMinLength)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("... ,,,")]
    [InlineData("& - $")]
    public void WordPatterns_NothingLeft_IsEmpty(string search)
    {
        Assert.Empty(DocumentSearchTerms.WordPatterns(search, DocumentSearchTerms.WordsPrefixMinLength));
    }

    // A word boundary cannot match next to punctuation, so a term is trimmed to its word characters: the order number
    // and the amount still match as words; "&" alone is nothing to match. Interior metacharacters are escaped.
    [Theory]
    [InlineData("#702-1234567", @"\m702-1234567")]
    [InlineData("$120", @"\m120\M")]
    [InlineData("Home & Garden", @"\mHome|\mGarden")]
    [InlineData("50%", @"\m50\M")]
    [InlineData("a*b", @"\ma\*b\M")]
    public void WordPatterns_TrimPunctuationAroundATerm_AndEscapeTheRest(string search, string expected)
    {
        Assert.Equal(expected, string.Join("|", DocumentSearchTerms.WordPatterns(search, 4)));
    }

    [Fact]
    public void WordPatterns_KeepAtMostMaxTerms()
    {
        var search = string.Join(" ", Enumerable.Range(0, DocumentSearchTerms.MaxTerms + 5).Select(i => $"term{i}"));

        Assert.Equal(DocumentSearchTerms.MaxTerms, DocumentSearchTerms.WordPatterns(search, 4).Count);
    }

    [Fact]
    public void Truncate_CutsToMaxSearchLength_NeverInsideASurrogatePair()
    {
        var plain = new string('a', DocumentSearchTerms.MaxSearchLength + 10);
        var emojiAtTheCut = new string('a', DocumentSearchTerms.MaxSearchLength - 1) + "😀" + "zzz";

        Assert.Equal(DocumentSearchTerms.MaxSearchLength, DocumentSearchTerms.Truncate(plain).Length);
        Assert.Equal(DocumentSearchTerms.MaxSearchLength - 1, DocumentSearchTerms.Truncate(emojiAtTheCut).Length);
        var short5 = plain[..5];
        Assert.Same(short5, DocumentSearchTerms.Truncate(short5));
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
    public void PredicateBuilder_Or_And_CombineOntoOneParameter()
    {
        Expression<Func<int, bool>> even = n => n % 2 == 0;
        Expression<Func<int, bool>> big = m => m > 10;

        var any = PredicateBuilder.Or(even, big).Compile();
        var all = PredicateBuilder.And(even, big).Compile();

        Assert.Equal((true, true, false), (any(4), any(11), any(3)));
        Assert.Equal((false, false, true), (all(4), all(11), all(12)));
        Assert.Equal((false, true), (PredicateBuilder.Or<int>().Compile()(1), PredicateBuilder.And<int>().Compile()(1)));
    }

    [Fact]
    public void PredicateBuilder_Compose_And_Any_InlineThePredicate()
    {
        Expression<Func<string, bool>> isLong = s => s.Length > 3;
        var nameIsLong = PredicateBuilder.Compose<(string Name, string[] Tags), string>(x => x.Name, isLong);
        var anyTagIsLong = PredicateBuilder.Any<(string Name, string[] Tags), string>(x => x.Tags, isLong);

        Assert.Equal((true, false), (nameIsLong.Compile()(("abcd", [])), nameIsLong.Compile()(("ab", []))));
        Assert.Equal((true, false), (anyTagIsLong.Compile()(("", ["a", "abcd"])), anyTagIsLong.Compile()(("", ["a"]))));
        // No Invoke node: EF must see the lambda as if written by hand.
        Assert.DoesNotContain("Invoke", nameIsLong.ToString() + anyTagIsLong.ToString());
    }
}
