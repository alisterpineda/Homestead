using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class CaseInsensitiveMatchTests
{
    private sealed record Row(int Id, string Name);

    [Fact]
    public void Choose_ExactCasing_WinsOverACaseInsensitiveMatch()
    {
        Row[] rows = [new(1, "ACME"), new(2, "Acme")];

        var result = CaseInsensitiveMatch.Choose(["Acme"], rows, r => r.Id, r => r.Name);

        Assert.Equal(new Row(2, "Acme"), Assert.Single(result.Known));
    }

    [Fact]
    public void Choose_NoExactCasing_TakesTheOrdinallyFirstCasing()
    {
        Row[] rows = [new(2, "Acme"), new(1, "ACME")];

        var result = CaseInsensitiveMatch.Choose(["acme"], rows, r => r.Id, r => r.Name);

        Assert.Equal(new Row(1, "ACME"), Assert.Single(result.Known));
    }

    [Fact]
    public void Choose_TheSameNameInTwoCasings_YieldsOneRow()
    {
        Row[] rows = [new(1, "Acme")];

        var result = CaseInsensitiveMatch.Choose(["Acme", "ACME"], rows, r => r.Id, r => r.Name);

        Assert.Equal(new Row(1, "Acme"), Assert.Single(result.Known));
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Choose_TwoNamesResolvingToOneKey_CollapseToTheFirst()
    {
        Row[] rows = [new(7, "Acme"), new(7, "Acme Inc")];

        var result = CaseInsensitiveMatch.Choose(["acme inc", "acme"], rows, r => r.Id, r => r.Name);

        Assert.Equal(new Row(7, "Acme Inc"), Assert.Single(result.Known));
    }

    [Fact]
    public void Choose_Unknowns_KeepRequestOrder_WithoutDuplicates_AndKnownOnesStillResolve()
    {
        Row[] rows = [new(1, "Jane Doe")];

        var result = CaseInsensitiveMatch.Choose(
            ["Rogers", "jane doe", "Bell", "rogers"], rows, r => r.Id, r => r.Name);

        Assert.Equal([new Row(1, "Jane Doe")], result.Known);
        Assert.Equal(["Rogers", "Bell"], result.Unknown);
    }

    [Fact]
    public void WhereNameIn_MatchesEveryCasingOfARequestedName_AndNothingElse()
    {
        var rows = new[] { new Row(1, "ACME"), new Row(2, "Acme"), new Row(3, "Acme Inc"), new Row(4, "Rogers") }
            .AsQueryable();

        var matched = rows.WhereNameIn(r => r.Name, ["acme"]).Select(r => r.Id).ToList();

        Assert.Equal([1, 2], matched);
    }
}
