using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Mcp;
using ModelContextProtocol;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class TagPathSnapperTests : IDisposable
{
    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly TagPathSnapper _sut;
    private readonly Dictionary<string, Guid> _ids = new();

    public TagPathSnapperTests()
    {
        foreach (var path in new[]
                 {
                     "Home", "Home/Utilities", "Home/Utilities/Electricity", "Home/Utilities/Water", "Home/Insurance",
                     "Car", "Car/Insurance",
                 })
            Seed(path);
        _db.SaveChanges();
        _sut = new TagPathSnapper(_db);
    }

    public void Dispose() => _db.Dispose();

    private void Seed(string path)
    {
        var id = Guid.NewGuid();
        _ids[path] = id;
        _db.Tags.Add(new Tag { Id = id, Name = path.Split('/')[^1], Path = path });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task KnownPath_InAnotherCasing_SnapsToTheStoredCasing()
    {
        var result = await _sut.SnapAsync(["home/UTILITIES/electricity"], Ct);

        var tag = Assert.Single(result.Known);
        Assert.Equal("Home/Utilities/Electricity", tag.Path);
        Assert.Equal(_ids["Home/Utilities/Electricity"], tag.Id);
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public async Task Path_IsNormalised_LikeATagWrite()
    {
        var result = await _sut.SnapAsync([" /Home/Utilities/ "], Ct);

        Assert.Equal("Home/Utilities", Assert.Single(result.Known).Path);
    }

    [Fact]
    public async Task EmptyPath_IsRejectedAsInvalidInput()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SnapAsync([" / "], Ct));
    }

    [Fact]
    public async Task ExactCasing_WinsOverAnotherCasingOfTheSamePath()
    {
        // Tag.Path's unique index is case-sensitive, so two casings can coexist.
        Seed("car");
        await _db.SaveChangesAsync(Ct);

        var result = await _sut.SnapAsync(["car"], Ct);

        Assert.Equal(_ids["car"], Assert.Single(result.Known).Id);
    }

    [Fact]
    public async Task SamePathTwice_InDifferentCasings_YieldsOneTag()
    {
        var result = await _sut.SnapAsync(["Car", "CAR"], Ct);

        Assert.Equal(_ids["Car"], Assert.Single(result.Known).Id);
    }

    [Fact]
    public async Task UnknownPaths_AreListed_KnownOnesStillResolve()
    {
        var result = await _sut.SnapAsync(["Home/Utilities/Gas", "car", "Pets/Dog"], Ct);

        Assert.Equal(["Car"], result.Known.Select(t => t.Path));
        Assert.Equal(["Home/Utilities/Gas", "Pets/Dog"], result.Unknown);
    }

    [Fact]
    public async Task Nearest_ForAMissingLeaf_IsItsSiblings()
    {
        var result = await _sut.SnapAsync(["Home/Utilities/Gas"], Ct);

        var (unknown, nearest) = Assert.Single(result.Suggestions);
        Assert.Equal("Home/Utilities/Gas", unknown);
        Assert.Equal(["Home/Utilities/Electricity", "Home/Utilities/Water"], nearest.Order());
    }

    [Fact]
    public async Task Nearest_ForATruncatedPath_ListsCaseInsensitivePrefixMatchesFirst_AtMostThree()
    {
        // Prefix matches: Home/Utilities and its two children; sibling Home/Insurance shares less of the path.
        var result = await _sut.SnapAsync(["home/util"], Ct);

        var (_, nearest) = Assert.Single(result.Suggestions);
        Assert.Equal(3, nearest.Length);
        Assert.Equal("Home/Utilities", nearest[0]);
        Assert.DoesNotContain("Home/Insurance", nearest);
    }

    // A misspelled parent has no siblings and is nobody's prefix; the trigram fallback still finds the path meant.
    [Fact]
    public async Task Nearest_ForAMisspelledParent_FallsBackToTheClosestPathsByTrigram()
    {
        var result = await _sut.SnapAsync(["Home/Utilitis/Water"], Ct);

        var (_, nearest) = Assert.Single(result.Suggestions);
        Assert.Equal("Home/Utilities/Water", nearest[0]);
        Assert.Contains("Home/Utilities", nearest);
    }

    [Fact]
    public async Task Nearest_IsOmitted_WhenNothingSharesTheParentOrPrefix()
    {
        var result = await _sut.SnapAsync(["Pets/Dog"], Ct);

        Assert.Equal(["Pets/Dog"], result.Unknown);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public async Task RequireKnown_AllKnown_ReturnsTheTags()
    {
        var tags = await _sut.RequireKnownAsync(["car/insurance", "Home"], Ct);

        Assert.Equal(["Car/Insurance", "Home"], tags.Select(t => t.Path));
    }

    [Fact]
    public async Task RequireKnown_OneUnknownWithNearest_NamesTheNearestPaths()
    {
        var e = await Assert.ThrowsAsync<McpException>(() => _sut.RequireKnownAsync(["Home/Utilities/Electricty"], Ct));

        Assert.Equal(
            "Unknown tag path(s): Home/Utilities/Electricty — nearest: Home/Utilities/Electricity, Home/Utilities/Water. " +
            "Use list_tags; call create_tag only if the user asked for a new tag.",
            e.Message);
    }

    [Fact]
    public async Task RequireKnown_SeveralUnknowns_GivesNearestPerUnknownThatHasAny()
    {
        var e = await Assert.ThrowsAsync<McpException>(() =>
            _sut.RequireKnownAsync(["Home/Utilities/Electricty", "Car", "Pets/Dog"], Ct));

        Assert.Equal(
            "Unknown tag path(s): Home/Utilities/Electricty, Pets/Dog — nearest: " +
            "Home/Utilities/Electricty → Home/Utilities/Electricity, Home/Utilities/Water. " +
            "Use list_tags; call create_tag only if the user asked for a new tag.",
            e.Message);
    }

    [Fact]
    public async Task RequireKnown_UnknownWithoutNearest_OmitsTheNearestClause()
    {
        var e = await Assert.ThrowsAsync<McpException>(() => _sut.RequireKnownAsync(["Pets/Dog"], Ct));

        Assert.Equal(
            "Unknown tag path(s): Pets/Dog. Use list_tags; call create_tag only if the user asked for a new tag.",
            e.Message);
    }
}
