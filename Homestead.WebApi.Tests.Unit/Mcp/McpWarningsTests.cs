using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpWarningsTests
{
    [Theory]
    [InlineData(25, 25, "")]
    [InlineData(100, 100, "")]
    [InlineData(1, 1, "")]
    [InlineData(101, 100, "page_size 101 lowered to 100.")]
    [InlineData(9999, 100, "page_size 9999 lowered to 100.")]
    [InlineData(0, 1, "page_size 0 raised to 1.")]
    [InlineData(-5, 1, "page_size -5 raised to 1.")]
    public void Clamp_KeepsTheValueInRange_AndSaysSoWhenItMoved(int requested, int expected, string warning)
    {
        var warnings = new McpWarnings();

        var value = warnings.Clamp("page_size", requested, 1, 100);

        Assert.Equal((expected, warning), (value, string.Join("|", warnings.Items)));
    }

    [Fact]
    public void Items_ListWarningsInTheOrderTheyWereAdded()
    {
        var warnings = new McpWarnings();
        warnings.Clamp("page", 0, 1, int.MaxValue);
        warnings.Add("sort_dir is ignored.");
        warnings.Clamp("page_size", 500, 1, 100);

        Assert.Equal(["page 0 raised to 1.", "sort_dir is ignored.", "page_size 500 lowered to 100."], warnings.Items);
    }
}
