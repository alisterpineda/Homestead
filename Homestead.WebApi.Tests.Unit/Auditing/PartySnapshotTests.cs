using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Parties;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class PartySnapshotTests
{
    [Fact]
    public void From_MapsAllFields()
    {
        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = "Acme Corp",
            Description = "A supplier"
        };

        var snapshot = PartySnapshot.From(party);

        Assert.Equal("Acme Corp", snapshot.Name);
        Assert.Equal("A supplier", snapshot.Description);
    }

    [Fact]
    public void Diff_DetectsNameChange()
    {
        var before = new PartySnapshot { Name = "Old", Description = null };
        var after = new PartySnapshot { Name = "New", Description = null };

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
        var before = new PartySnapshot { Name = "Party", Description = "Old desc" };
        var after = new PartySnapshot { Name = "Party", Description = "New desc" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Description", scalar.Field);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = new PartySnapshot { Name = "Party", Description = "Desc" };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }
}
