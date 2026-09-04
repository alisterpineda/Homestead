using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class ItemNoteSnapshotTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_MapsAllFields()
    {
        var note = new ItemNote
        {
            Id = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            OccurredAt = Day1,
            Body = "5W-30, 4.5 L",
        };

        var snapshot = ItemNoteSnapshot.From(note);

        Assert.Equal(Day1, snapshot.OccurredAt);
        Assert.Equal("5W-30, 4.5 L", snapshot.Body);
    }

    [Fact]
    public void Diff_DetectsOccurredAtChange()
    {
        var before = new ItemNoteSnapshot { OccurredAt = Day1, Body = "B" };
        var after = new ItemNoteSnapshot { OccurredAt = Day2, Body = "B" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("OccurredAt", scalar.Field);
        Assert.Equal(Day1.ToString("O"), scalar.OldValue);
        Assert.Equal(Day2.ToString("O"), scalar.NewValue);
    }

    [Fact]
    public void Diff_DetectsBodyChange()
    {
        var before = new ItemNoteSnapshot { OccurredAt = Day1, Body = "Old" };
        var after = new ItemNoteSnapshot { OccurredAt = Day1, Body = "New" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Body", scalar.Field);
        Assert.Equal("Old", scalar.OldValue);
        Assert.Equal("New", scalar.NewValue);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = new ItemNoteSnapshot { OccurredAt = Day1, Body = "B" };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_DetectsMultipleChanges()
    {
        var before = new ItemNoteSnapshot { OccurredAt = Day1, Body = "Old" };
        var after = new ItemNoteSnapshot { OccurredAt = Day2, Body = "New" };

        var changes = before.Diff(after);

        Assert.Equal(2, changes.Count);
    }
}
