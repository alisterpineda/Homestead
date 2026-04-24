using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class CommoditySnapshotTests
{
    private static Commodity CreateCommodity() => new()
    {
        Id = Guid.NewGuid(),
        Symbol = "VEQT",
        Exchange = "TSX",
        Name = "Vanguard All-Equity",
        Kind = CommodityKind.Security,
        DisplayPrecision = 4,
        IsSystemManaged = false,
    };

    [Fact]
    public void From_MapsAllAuditedFields()
    {
        var commodity = CreateCommodity();

        var snapshot = CommoditySnapshot.From(commodity);

        Assert.Equal("VEQT", snapshot.Symbol);
        Assert.Equal("TSX", snapshot.Exchange);
        Assert.Equal("Vanguard All-Equity", snapshot.Name);
        Assert.Equal(CommodityKind.Security, snapshot.Kind);
        Assert.Equal((short)4, snapshot.DisplayPrecision);
        Assert.False(snapshot.IsSystemManaged);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = CommoditySnapshot.From(CreateCommodity());
        Assert.Empty(snapshot.Diff(snapshot));
    }

    [Fact]
    public void Diff_DetectsNameAndPrecisionChanges()
    {
        var before = CommoditySnapshot.From(CreateCommodity());
        var after = before with { Name = "New Name", DisplayPrecision = 2 };

        var changes = before.Diff(after);

        Assert.Equal(2, changes.Count);
        var nameChange = Assert.IsType<ScalarFieldChange>(
            Assert.Single(changes, c => c.Field == "Name"));
        Assert.Equal("Vanguard All-Equity", nameChange.OldValue);
        Assert.Equal("New Name", nameChange.NewValue);

        var precisionChange = Assert.IsType<ScalarFieldChange>(
            Assert.Single(changes, c => c.Field == "DisplayPrecision"));
        Assert.Equal("4", precisionChange.OldValue);
        Assert.Equal("2", precisionChange.NewValue);
    }

    [Fact]
    public void Diff_DetectsExchangeChange()
    {
        var before = CommoditySnapshot.From(CreateCommodity());
        var after = before with { Exchange = "NASDAQ" };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("Exchange", change.Field);
        Assert.Equal("TSX", change.OldValue);
        Assert.Equal("NASDAQ", change.NewValue);
    }

    [Fact]
    public void Diff_DetectsIsSystemManagedChange()
    {
        var before = CommoditySnapshot.From(CreateCommodity());
        var after = before with { IsSystemManaged = true };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("IsSystemManaged", change.Field);
        Assert.Equal("False", change.OldValue);
        Assert.Equal("True", change.NewValue);
    }
}
