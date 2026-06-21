using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Modules.Search.Features.UniversalSearch;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Search;

public class UniversalSearchHandlerTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static ISearchSource FakeSource(string source, string label)
    {
        var src = Substitute.For<ISearchSource>();
        src.Source.Returns(source);
        src.Label.Returns(label);
        return src;
    }

    private static SearchHit Hit(string source, string id, string title) =>
        new(source, "durable", id, title, null, null);

    private static UniversalSearchHandler Sut(params ISearchSource[] sources) =>
        new(sources, NullLogger<UniversalSearchHandler>.Instance);

    [Fact]
    public async Task Handle_QueryShorterThanTwoChars_ReturnsNoGroupsAndDoesNotFanOut()
    {
        var src = FakeSource("inventory", "Inventory");
        var sut = Sut(src);

        var result = await sut.Handle(new UniversalSearchQuery { Q = " a " }, CT);

        Assert.Equal("a", result.Query);
        Assert.Empty(result.Groups);
        await src.DidNotReceiveWithAnyArgs()
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullQuery_ReturnsNoGroups()
    {
        var src = FakeSource("inventory", "Inventory");
        var sut = Sut(src);

        var result = await sut.Handle(new UniversalSearchQuery { Q = null }, CT);

        Assert.Empty(result.Groups);
        await src.DidNotReceiveWithAnyArgs()
            .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AggregatesEachSourceIntoItsOwnGroup()
    {
        var inv = FakeSource("inventory", "Inventory");
        inv.SearchAsync("drill", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SearchSourceResult(2, [Hit("inventory", "1", "Drill"), Hit("inventory", "2", "Drill bit")]));

        var docs = FakeSource("documents", "Documents");
        docs.SearchAsync("drill", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SearchSourceResult(1, [Hit("documents", "9", "Drill manual")]));

        var sut = Sut(inv, docs);

        var result = await sut.Handle(new UniversalSearchQuery { Q = "drill" }, CT);

        Assert.Equal(2, result.Groups.Count);

        var first = result.Groups[0];
        Assert.Equal("inventory", first.Source);
        Assert.Equal("Inventory", first.Label);
        Assert.Equal(2, first.Total);
        Assert.Equal(2, first.Results.Count);

        var second = result.Groups[1];
        Assert.Equal("documents", second.Source);
        Assert.Equal(1, second.Total);
        Assert.Single(second.Results);
    }

    [Fact]
    public async Task Handle_SourceThatThrows_IsOmittedAndOthersSurvive()
    {
        var bad = FakeSource("inventory", "Inventory");
        bad.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var good = FakeSource("documents", "Documents");
        good.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SearchSourceResult(1, [Hit("documents", "9", "Drill manual")]));

        var sut = Sut(bad, good);

        var result = await sut.Handle(new UniversalSearchQuery { Q = "drill" }, CT);

        var group = Assert.Single(result.Groups);
        Assert.Equal("documents", group.Source);
    }

    [Fact]
    public async Task Handle_GroupsWithZeroTotal_AreExcluded()
    {
        var empty = FakeSource("inventory", "Inventory");
        empty.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SearchSourceResult(0, []));

        var sut = Sut(empty);

        var result = await sut.Handle(new UniversalSearchQuery { Q = "drill" }, CT);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Handle_DefaultLimit_IsSix()
    {
        var src = FakeSource("inventory", "Inventory");
        src.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SearchSourceResult(1, [Hit("inventory", "1", "Drill")]));
        var sut = Sut(src);

        await sut.Handle(new UniversalSearchQuery { Q = "drill" }, CT);

        await src.Received(1).SearchAsync("drill", 6, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(50, 20)]   // above max → clamped to 20
    [InlineData(0, 1)]     // below min → clamped to 1
    [InlineData(-3, 1)]    // negative → clamped to 1
    [InlineData(10, 10)]   // in range → passed through
    public async Task Handle_LimitIsClamped(int requested, int expected)
    {
        var src = FakeSource("inventory", "Inventory");
        src.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SearchSourceResult(1, [Hit("inventory", "1", "Drill")]));
        var sut = Sut(src);

        await sut.Handle(new UniversalSearchQuery { Q = "drill", Limit = requested }, CT);

        await src.Received(1).SearchAsync("drill", expected, Arg.Any<CancellationToken>());
    }
}
