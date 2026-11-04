using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Modules.Mcp;
using ModelContextProtocol;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class PartyNameResolverTests : IDisposable
{
    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly PartyNameResolver _sut;
    private readonly Dictionary<string, Guid> _ids = new();

    public PartyNameResolverTests()
    {
        foreach (var name in new[] { "Toronto Hydro", "Jane Doe", "Acme" })
            Seed(name);
        _db.SaveChanges();
        _sut = new PartyNameResolver(_db);
    }

    public void Dispose() => _db.Dispose();

    private void Seed(string name)
    {
        var id = Guid.NewGuid();
        _ids[name] = id;
        _db.Parties.Add(new Party { Id = id, Name = name });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Name_InAnotherCasing_AndPadded_ResolvesToTheStoredParty()
    {
        var result = await _sut.ResolveAsync(["  toronto HYDRO "], Ct);

        Assert.Equal(new ResolvedParty(_ids["Toronto Hydro"], "Toronto Hydro"), Assert.Single(result.Known));
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public async Task MatchIsExact_NotAPrefixOrSubstring()
    {
        var result = await _sut.ResolveAsync(["Toronto"], Ct);

        Assert.Empty(result.Known);
        Assert.Equal(["Toronto"], result.Unknown);
    }

    [Fact]
    public async Task UnknownNames_AreListed_KnownOnesStillResolve()
    {
        var result = await _sut.ResolveAsync(["Bell Canada", "jane doe", "Rogers"], Ct);

        Assert.Equal(["Jane Doe"], result.Known.Select(p => p.Name));
        Assert.Equal(["Bell Canada", "Rogers"], result.Unknown);
    }

    [Fact]
    public async Task ExactCasing_WinsOverAnotherCasingOfTheSameName()
    {
        // Party.Name's unique index is case-sensitive, so two casings can coexist.
        Seed("ACME");
        await _db.SaveChangesAsync(Ct);

        var result = await _sut.ResolveAsync(["ACME"], Ct);

        Assert.Equal(_ids["ACME"], Assert.Single(result.Known).Id);
    }

    [Fact]
    public async Task SameNameTwice_YieldsOneParty()
    {
        var result = await _sut.ResolveAsync(["Acme", "acme"], Ct);

        Assert.Equal(_ids["Acme"], Assert.Single(result.Known).Id);
    }

    [Fact]
    public async Task BlankName_IsRejectedAsInvalidInput()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ResolveAsync(["  "], Ct));
    }

    [Fact]
    public void NormalizeName_TrimsTheName() =>
        Assert.Equal("Toronto Hydro", PartyNameResolver.NormalizeName("  Toronto Hydro "));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeName_Blank_IsInvalidInput(string name)
    {
        var e = Assert.Throws<ArgumentException>(() => PartyNameResolver.NormalizeName(name));
        Assert.Equal("Party name cannot be empty.", e.Message);
    }

    [Fact]
    public async Task RequireKnown_AllKnown_ReturnsThem()
    {
        var parties = await _sut.RequireKnownAsync(["acme", "Jane Doe"], Ct);

        Assert.Equal([_ids["Acme"], _ids["Jane Doe"]], parties.Select(p => p.Id));
    }

    [Fact]
    public async Task RequireKnown_UnknownWithANearName_NamesIt()
    {
        var e = await Assert.ThrowsAsync<McpException>(() => _sut.RequireKnownAsync(["Torronto Hydro"], Ct));

        Assert.Equal(
            "Unknown party name(s): Torronto Hydro — nearest: Toronto Hydro. " +
            "Use list_parties; call create_party only if the user asked for a new party.",
            e.Message);
    }

    [Fact]
    public async Task RequireKnown_Unknown_ThrowsWithTheNamesAndTheGuidance()
    {
        var e = await Assert.ThrowsAsync<McpException>(() =>
            _sut.RequireKnownAsync(["Bell Canada", "Acme", "Rogers"], Ct));

        Assert.Equal(
            "Unknown party name(s): Bell Canada, Rogers. Use list_parties; call create_party only if the user asked for a new party.",
            e.Message);
    }
}
