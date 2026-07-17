using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class PayeeSnapshotTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static Payee CreatePayee() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Costco",
        DefaultCategoryId = CategoryId,
        Notes = "Warehouse club",
        IsArchived = false,
    };

    [Fact]
    public void From_MapsAllAuditedFields()
    {
        var payee = CreatePayee();

        var snapshot = PayeeSnapshot.From(payee);

        Assert.Equal("Costco", snapshot.Name);
        Assert.Equal(CategoryId, snapshot.DefaultCategoryId);
        Assert.Equal("Warehouse club", snapshot.Notes);
        Assert.False(snapshot.IsArchived);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = PayeeSnapshot.From(CreatePayee());
        Assert.Empty(snapshot.Diff(snapshot));
    }

    [Fact]
    public void Diff_DetectsNameAndNotesChanges()
    {
        var before = PayeeSnapshot.From(CreatePayee());
        var after = before with { Name = "Costco Wholesale", Notes = "Updated" };

        var changes = before.Diff(after);

        Assert.Equal(2, changes.Count);
        var nameChange = Assert.IsType<ScalarFieldChange>(
            Assert.Single(changes, c => c.Field == "Name"));
        Assert.Equal("Costco", nameChange.OldValue);
        Assert.Equal("Costco Wholesale", nameChange.NewValue);

        var notesChange = Assert.IsType<ScalarFieldChange>(
            Assert.Single(changes, c => c.Field == "Notes"));
        Assert.Equal("Warehouse club", notesChange.OldValue);
        Assert.Equal("Updated", notesChange.NewValue);
    }

    [Fact]
    public void Diff_DetectsDefaultCategoryChange_ValueToNull()
    {
        var before = PayeeSnapshot.From(CreatePayee());
        var after = before with { DefaultCategoryId = null };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("DefaultCategoryId", change.Field);
        Assert.Equal(CategoryId.ToString(), change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void Diff_DetectsDefaultCategoryChange_NullToValue()
    {
        var newCategory = Guid.NewGuid();
        var before = PayeeSnapshot.From(CreatePayee()) with { DefaultCategoryId = null };
        var after = before with { DefaultCategoryId = newCategory };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("DefaultCategoryId", change.Field);
        Assert.Null(change.OldValue);
        Assert.Equal(newCategory.ToString(), change.NewValue);
    }

    [Fact]
    public void Diff_DetectsIsArchivedFlip()
    {
        var before = PayeeSnapshot.From(CreatePayee());
        var after = before with { IsArchived = true };

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("IsArchived", change.Field);
        Assert.Equal("False", change.OldValue);
        Assert.Equal("True", change.NewValue);
    }
}
