using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class AccountSnapshotTests
{
    private static Account CreateAccount() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Chequing",
        AccountType = AccountType.Asset,
        ParentId = null,
        DefaultCommodityId = Guid.NewGuid(),
        IsMultiPosition = false,
        Institution = "Big Bank",
        IsSystem = false,
        IsArchived = false,
    };

    [Fact]
    public void From_MapsMutableFields()
    {
        var account = CreateAccount();

        var snapshot = AccountSnapshot.From(account);

        Assert.Equal(account.Name, snapshot.Name);
        Assert.Equal(account.ParentId, snapshot.ParentId);
        Assert.Equal(account.Institution, snapshot.Institution);
        Assert.Equal(account.IsArchived, snapshot.IsArchived);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = AccountSnapshot.From(CreateAccount());
        Assert.Empty(snapshot.Diff(snapshot));
    }

    [Fact]
    public void Diff_DetectsNameAndArchivedChanges()
    {
        var before = AccountSnapshot.From(CreateAccount());
        var after = before with { Name = "Renamed", IsArchived = true };

        var changes = before.Diff(after);

        Assert.Equal(2, changes.Count);
        var nameChange = Assert.IsType<ScalarFieldChange>(
            Assert.Single(changes, c => c.Field == "Name"));
        Assert.Equal("Chequing", nameChange.OldValue);
        Assert.Equal("Renamed", nameChange.NewValue);

        var archivedChange = Assert.IsType<ScalarFieldChange>(
            Assert.Single(changes, c => c.Field == "IsArchived"));
        Assert.Equal("False", archivedChange.OldValue);
        Assert.Equal("True", archivedChange.NewValue);
    }

    [Fact]
    public void Diff_DetectsParentIdChange_FromNullToValue()
    {
        var before = AccountSnapshot.From(CreateAccount());
        var newParent = Guid.NewGuid();
        var after = before with { ParentId = newParent };

        var changes = before.Diff(after);

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(changes));
        Assert.Equal("ParentId", change.Field);
        Assert.Null(change.OldValue);
        Assert.Equal(newParent.ToString(), change.NewValue);
    }
}
