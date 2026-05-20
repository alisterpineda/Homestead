using Homestead.WebApi.Common;

namespace Homestead.WebApi.Tests.Unit.Common;

public class PaginatedRequestTests
{
    [Fact]
    public void Defaults_AreFirstPageAndFiftyPerPage()
    {
        var request = new PaginatedRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(50, request.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Page_BelowOne_IsClampedToOne(int input, int expected)
    {
        var request = new PaginatedRequest { Page = input };

        Assert.Equal(expected, request.Page);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(1000, 1000)]
    [InlineData(1001, 1000)]
    [InlineData(int.MaxValue, 1000)]
    public void PageSize_IsClampedToOneToOneThousand(int input, int expected)
    {
        var request = new PaginatedRequest { PageSize = input };

        Assert.Equal(expected, request.PageSize);
    }
}
