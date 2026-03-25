using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class TagSnapshotTests
{
    [Fact]
    public void From_MapsAllFields()
    {
        var parentId = Guid.NewGuid();
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = "Invoices",
            Path = "Finance/Invoices",
            ParentId = parentId,
            Color = "#ff0000"
        };

        var snapshot = TagSnapshot.From(tag);

        Assert.Equal("Invoices", snapshot.Name);
        Assert.Equal("Finance/Invoices", snapshot.Path);
        Assert.Equal(parentId, snapshot.ParentId);
        Assert.Equal("#ff0000", snapshot.Color);
    }

    [Fact]
    public void Diff_DetectsNameChange()
    {
        var before = new TagSnapshot { Name = "Old", Path = "Old", ParentId = null, Color = null };
        var after = new TagSnapshot { Name = "New", Path = "Old", ParentId = null, Color = null };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Name", scalar.Field);
        Assert.Equal("Old", scalar.OldValue);
        Assert.Equal("New", scalar.NewValue);
    }

    [Fact]
    public void Diff_DetectsPathChange()
    {
        var before = new TagSnapshot { Name = "Tag", Path = "A/Tag", ParentId = null, Color = null };
        var after = new TagSnapshot { Name = "Tag", Path = "B/Tag", ParentId = null, Color = null };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Path", scalar.Field);
    }

    [Fact]
    public void Diff_DetectsParentIdChange()
    {
        var oldParent = Guid.NewGuid();
        var newParent = Guid.NewGuid();
        var before = new TagSnapshot { Name = "Tag", Path = "Tag", ParentId = oldParent, Color = null };
        var after = new TagSnapshot { Name = "Tag", Path = "Tag", ParentId = newParent, Color = null };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("ParentId", scalar.Field);
    }

    [Fact]
    public void Diff_DetectsColorChange()
    {
        var before = new TagSnapshot { Name = "Tag", Path = "Tag", ParentId = null, Color = "#000" };
        var after = new TagSnapshot { Name = "Tag", Path = "Tag", ParentId = null, Color = "#fff" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Color", scalar.Field);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var parentId = Guid.NewGuid();
        var snapshot = new TagSnapshot { Name = "Tag", Path = "Tag", ParentId = parentId, Color = "#abc" };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }
}
