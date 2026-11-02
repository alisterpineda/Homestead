using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpPagingTests
{
    [Theory]
    [InlineData(25, 100, 25)]
    [InlineData(100, 100, 100)]
    [InlineData(101, 100, 100)]
    [InlineData(1, 100, 1)]
    [InlineData(0, 100, 1)]
    [InlineData(-7, 100, 1)]
    [InlineData(1000, 500, 500)]
    public void ClampPageSize_KeepsTheSizeWithinOneAndTheToolsMax(int requested, int max, int expected) =>
        Assert.Equal(expected, McpPaging.ClampPageSize(requested, max));

    [Theory]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    public void ClampPage_StartsAtOne(int requested, int expected) =>
        Assert.Equal(expected, McpPaging.ClampPage(requested));
}
