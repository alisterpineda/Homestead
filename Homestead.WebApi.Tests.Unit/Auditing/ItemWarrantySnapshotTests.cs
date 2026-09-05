using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class ItemWarrantySnapshotTests
{
    private static readonly DateOnly Day1 = new(2028, 3, 12);
    private static readonly DateOnly Day2 = new(2029, 3, 12);

    [Fact]
    public void From_MapsAllFields()
    {
        var warranty = new ItemWarranty
        {
            Id = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            Provider = "Bosch",
            ExpiresOn = Day1,
            Notes = "motor only",
        };

        var snapshot = ItemWarrantySnapshot.From(warranty);

        Assert.Equal("Bosch", snapshot.Provider);
        Assert.Equal(Day1, snapshot.ExpiresOn);
        Assert.Equal("motor only", snapshot.Notes);
    }

    [Fact]
    public void From_KeepsNullsAsNull()
    {
        var snapshot = ItemWarrantySnapshot.From(new ItemWarranty { Id = Guid.NewGuid(), ItemId = Guid.NewGuid() });

        Assert.Null(snapshot.Provider);
        Assert.Null(snapshot.ExpiresOn);
        Assert.Null(snapshot.Notes);
    }

    [Fact]
    public void Diff_DetectsProviderChange()
    {
        var before = new ItemWarrantySnapshot { Provider = "Bosch", ExpiresOn = Day1 };
        var after = new ItemWarrantySnapshot { Provider = "Home Depot", ExpiresOn = Day1 };

        var scalar = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("Provider", scalar.Field);
        Assert.Equal("Bosch", scalar.OldValue);
        Assert.Equal("Home Depot", scalar.NewValue);
    }

    [Fact]
    public void Diff_FormatsExpiresOnAsIsoDate()
    {
        var before = new ItemWarrantySnapshot { ExpiresOn = Day1 };
        var after = new ItemWarrantySnapshot { ExpiresOn = Day2 };

        var scalar = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("ExpiresOn", scalar.Field);
        Assert.Equal("2028-03-12", scalar.OldValue);
        Assert.Equal("2029-03-12", scalar.NewValue);
    }

    [Fact]
    public void Diff_DetectsExpiryBecomingOpenEnded_AndBack()
    {
        var dated = new ItemWarrantySnapshot { ExpiresOn = Day1 };
        var openEnded = new ItemWarrantySnapshot { ExpiresOn = null };

        var cleared = Assert.IsType<ScalarFieldChange>(Assert.Single(dated.Diff(openEnded)));
        Assert.Equal("2028-03-12", cleared.OldValue);
        Assert.Null(cleared.NewValue);

        var set = Assert.IsType<ScalarFieldChange>(Assert.Single(openEnded.Diff(dated)));
        Assert.Null(set.OldValue);
        Assert.Equal("2028-03-12", set.NewValue);
    }

    [Fact]
    public void Diff_DetectsNotesChange()
    {
        var before = new ItemWarrantySnapshot { Notes = null };
        var after = new ItemWarrantySnapshot { Notes = "motor only" };

        var scalar = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("Notes", scalar.Field);
        Assert.Null(scalar.OldValue);
        Assert.Equal("motor only", scalar.NewValue);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = new ItemWarrantySnapshot { Provider = "Bosch", ExpiresOn = Day1, Notes = "n" };

        Assert.Empty(snapshot.Diff(snapshot));
    }

    [Fact]
    public void Diff_DetectsAllThreeAtOnce()
    {
        var before = new ItemWarrantySnapshot { Provider = "Bosch", ExpiresOn = Day1, Notes = "old" };
        var after = new ItemWarrantySnapshot { Provider = null, ExpiresOn = null, Notes = "new" };

        Assert.Equal(3, before.Diff(after).Count);
    }
}
