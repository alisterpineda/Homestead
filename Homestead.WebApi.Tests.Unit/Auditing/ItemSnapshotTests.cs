using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class ItemSnapshotTests
{
    [Fact]
    public void From_MapsBrandIdAndModel()
    {
        var brandId = Guid.NewGuid();
        var item = new Item
        {
            Name = "MacBook Pro",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            BrandId = brandId,
            Model = "MRW23",
        };

        var snapshot = ItemSnapshot.From(item);

        Assert.Equal(brandId, snapshot.BrandId);
        Assert.Equal("MRW23", snapshot.Model);
    }

    [Fact]
    public void Diff_DetectsBrandIdChange()
    {
        var oldBrand = Guid.NewGuid();
        var newBrand = Guid.NewGuid();
        var before = new ItemSnapshot { Name = "Item", Status = ItemStatus.Active, BrandId = oldBrand };
        var after = new ItemSnapshot { Name = "Item", Status = ItemStatus.Active, BrandId = newBrand };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("BrandId", scalar.Field);
        Assert.Equal(oldBrand.ToString(), scalar.OldValue);
        Assert.Equal(newBrand.ToString(), scalar.NewValue);
    }

    [Fact]
    public void Diff_DetectsModelChange()
    {
        var before = new ItemSnapshot { Name = "Item", Status = ItemStatus.Active, Model = "A" };
        var after = new ItemSnapshot { Name = "Item", Status = ItemStatus.Active, Model = "B" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Model", scalar.Field);
        Assert.Equal("A", scalar.OldValue);
        Assert.Equal("B", scalar.NewValue);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenBrandAndModelUnchanged()
    {
        var brandId = Guid.NewGuid();
        var snapshot = new ItemSnapshot
        {
            Name = "Item", Status = ItemStatus.Active, BrandId = brandId, Model = "MRW23",
        };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }
}
