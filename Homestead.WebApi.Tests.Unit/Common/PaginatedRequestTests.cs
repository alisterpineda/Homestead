using Homestead.WebApi.Common;

namespace Homestead.WebApi.Tests.Unit.Common;

public class PaginatedRequestTests
{
    [Fact]
    public void OmittedParams_EffectiveDefaultsAreFirstPageAndFiftyPerPage()
    {
        // A default-constructed request mirrors what [AsParameters] binds when page/pageSize are
        // absent from the query string: both null, so the Effective* accessors supply the defaults.
        var request = new PaginatedRequest();

        Assert.Null(request.Page);
        Assert.Null(request.PageSize);
        Assert.Equal(1, request.EffectivePage);
        Assert.Equal(50, request.EffectivePageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void EffectivePage_BelowOne_IsClampedToOne(int input, int expected)
    {
        var request = new PaginatedRequest { Page = input };

        Assert.Equal(expected, request.EffectivePage);
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
    public void EffectivePageSize_IsClampedToOneToOneThousand(int input, int expected)
    {
        var request = new PaginatedRequest { PageSize = input };

        Assert.Equal(expected, request.EffectivePageSize);
    }
}
