using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpNearestTests
{
    private static readonly string[] Parties = ["TD Canada Trust", "Toronto Hydro", "Jane Doe", "Acme", "Bell Canada"];

    [Fact]
    public void Find_AShortNameAgainstALongerOne_ScoresOnWhatTheyShare()
    {
        // "TD Bank" vs "TD Canada Trust": the "td" word is all they share, but it is enough of the query.
        Assert.Equal(["TD Canada Trust"], McpNearest.Find("TD Bank", Parties));
    }

    [Fact]
    public void Find_ATypo_FindsTheName()
    {
        Assert.Equal(["Toronto Hydro"], McpNearest.Find("Torronto Hydro", Parties));
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndIgnoresPunctuation()
    {
        Assert.Equal(["Toronto Hydro"], McpNearest.Find("toronto-hydro inc.", Parties));
    }

    [Fact]
    public void Find_NothingAlike_IsEmpty()
    {
        Assert.Empty(McpNearest.Find("Zephyrine Utilities", Parties));
    }

    [Fact]
    public void Find_ListsAtMostThree_BestFirst()
    {
        string[] paths = ["Home/Utilities/Water", "Home/Utilities", "Home/Utilities/Electricity", "Home/Utilities/Gas", "Car"];

        var nearest = McpNearest.Find("home/utilities/wter", paths);

        Assert.Equal(3, nearest.Length);
        Assert.DoesNotContain("Car", nearest);
        Assert.Equal("Home/Utilities/Water", nearest[0]);
    }

    [Fact]
    public void Score_IdenticalNames_IsOne_AndUnrelatedIsZero()
    {
        Assert.Equal((1.0, 0.0), (McpNearest.Score("Acme", "acme"), McpNearest.Score("Acme", "Zzz")));
    }

    [Fact]
    public void Describe_OneUnknown_ListsItsNearest_SeveralUnknowns_ListPerUnknown()
    {
        var one = McpNearest.Describe("party name(s)", ["TD Bank"], [("TD Bank", ["TD Canada Trust"])]);
        var several = McpNearest.Describe("tag path(s)", ["A", "B", "C"], [("A", ["A1", "A2"]), ("C", ["C1"])]);
        var none = McpNearest.Describe("party name(s)", ["Nobody"], []);

        Assert.Equal(
            ("Unknown party name(s): TD Bank — nearest: TD Canada Trust",
             "Unknown tag path(s): A, B, C — nearest: A → A1, A2; C → C1",
             "Unknown party name(s): Nobody"),
            (one, several, none));
    }
}
