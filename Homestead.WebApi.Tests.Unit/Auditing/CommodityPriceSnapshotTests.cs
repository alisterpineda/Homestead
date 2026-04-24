using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class CommodityPriceSnapshotTests
{
    private static CommodityPrice CreatePrice() => new()
    {
        Id = Guid.NewGuid(),
        CommodityId = Guid.NewGuid(),
        QuoteCurrencyId = Guid.NewGuid(),
        PriceAt = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero),
        Rate = 1.3456m,
        CreatedByTransactionId = null,
    };

    [Fact]
    public void From_MapsAllAuditedFields()
    {
        var price = CreatePrice();

        var snapshot = CommodityPriceSnapshot.From(price);

        Assert.Equal(price.CommodityId, snapshot.CommodityId);
        Assert.Equal(price.QuoteCurrencyId, snapshot.QuoteCurrencyId);
        Assert.Equal(price.PriceAt, snapshot.PriceAt);
        Assert.Equal(price.Rate, snapshot.Rate);
        Assert.Null(snapshot.CreatedByTransactionId);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = CommodityPriceSnapshot.From(CreatePrice());
        Assert.Empty(snapshot.Diff(snapshot));
    }

    [Fact]
    public void Diff_DetectsRateChange_UsesInvariantCulture()
    {
        var before = CommodityPriceSnapshot.From(CreatePrice());
        var after = before with { Rate = 1.4000m };

        var changes = before.Diff(after);

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(changes));
        Assert.Equal("Rate", change.Field);
        Assert.Equal("1.3456", change.OldValue);
        Assert.Equal("1.4000", change.NewValue);
    }

    [Fact]
    public void Diff_DetectsCreatedByTransactionIdChange()
    {
        var before = CommodityPriceSnapshot.From(CreatePrice());
        var txnId = Guid.NewGuid();
        var after = before with { CreatedByTransactionId = txnId };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("CreatedByTransactionId", change.Field);
        Assert.Null(change.OldValue);
        Assert.Equal(txnId.ToString(), change.NewValue);
    }

    [Fact]
    public void Diff_DetectsPriceAtChange()
    {
        var before = CommodityPriceSnapshot.From(CreatePrice());
        var after = before with { PriceAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero) };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("PriceAt", change.Field);
    }
}
