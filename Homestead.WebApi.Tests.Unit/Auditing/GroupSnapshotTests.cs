using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Groups;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class GroupSnapshotTests
{
    [Fact]
    public void From_MapsAllFields()
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Engineering",
            Description = "The engineering team"
        };

        var snapshot = GroupSnapshot.From(group);

        Assert.Equal("Engineering", snapshot.Name);
        Assert.Equal("The engineering team", snapshot.Description);
    }

    [Fact]
    public void Diff_DetectsNameChange()
    {
        var before = new GroupSnapshot { Name = "Old", Description = null };
        var after = new GroupSnapshot { Name = "New", Description = null };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Name", scalar.Field);
        Assert.Equal("Old", scalar.OldValue);
        Assert.Equal("New", scalar.NewValue);
    }

    [Fact]
    public void Diff_DetectsDescriptionChange()
    {
        var before = new GroupSnapshot { Name = "Group", Description = "Old" };
        var after = new GroupSnapshot { Name = "Group", Description = "New" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Description", scalar.Field);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = new GroupSnapshot { Name = "Group", Description = "Desc" };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }
}
