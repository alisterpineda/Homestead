using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Tests.Unit.Domains.Inventory;

// The shared constants and the row-level rule. The item-level rule ships only as SQL
// (DurableResponseMapper.For and GetInventoryOverviewHandler); GetDurablesHandlerTests and
// GetInventoryOverviewHandlerTests pin those.
public class WarrantyStatusRulesTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    [Fact]
    public void ExpiringWindow_IsThirtyDays()
    {
        Assert.Equal(30, WarrantyStatusRules.ExpiringWindowDays);
        Assert.Equal(new DateOnly(2026, 3, 31), WarrantyStatusRules.Cutoff(Today));
    }

    [Fact]
    public void TodayUtc_ReadsTheProvidersUtcDate()
    {
        var tp = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2026, 3, 1, 23, 59, 0, TimeSpan.Zero));

        Assert.Equal(Today, WarrantyStatusRules.TodayUtc(tp));
    }

    [Theory]
    [InlineData(null, WarrantyStatus.Active)]
    [InlineData("2026-02-28", WarrantyStatus.Expired)]
    [InlineData("2026-03-01", WarrantyStatus.Expiring)]
    [InlineData("2026-03-31", WarrantyStatus.Expiring)]
    [InlineData("2026-04-01", WarrantyStatus.Active)]
    [InlineData("2099-01-01", WarrantyStatus.Active)]
    public void ForRow_IsNeverNone(string? expiresOn, WarrantyStatus expected)
    {
        var date = expiresOn is null ? (DateOnly?)null : DateOnly.Parse(expiresOn);

        var status = WarrantyStatusRules.ForRow(date, Today);

        Assert.Equal(expected, status);
        Assert.NotEqual(WarrantyStatus.None, status);
    }
}
