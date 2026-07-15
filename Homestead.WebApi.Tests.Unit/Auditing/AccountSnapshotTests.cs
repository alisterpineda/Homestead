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
        Kind = AccountKind.Cash,
        ReportingCurrencyId = Guid.NewGuid(),
        Institution = "Big Bank",
        AccountNumber = "1234567890",
        IsSystem = false,
        IsArchived = false,
    };

    [Fact]
    public void From_MapsMutableFields()
    {
        var account = CreateAccount();

        var snapshot = AccountSnapshot.From(account);

        Assert.Equal(account.Name, snapshot.Name);
        Assert.Equal(account.Institution, snapshot.Institution);
        Assert.Equal(account.IsArchived, snapshot.IsArchived);
    }

    [Fact]
    public void From_CapturesMaskedAccountNumber_NotRaw()
    {
        var snapshot = AccountSnapshot.From(CreateAccount());

        // The audit snapshot must never persist a raw account number.
        Assert.Equal("••••7890", snapshot.AccountNumberMasked);
    }

    [Fact]
    public void Diff_DetectsAccountNumberChange_ReportedMasked()
    {
        var before = AccountSnapshot.From(CreateAccount());
        var after = AccountSnapshot.From(new Account
        {
            Id = Guid.NewGuid(),
            Name = "Chequing",
            AccountType = AccountType.Asset,
            Kind = AccountKind.Cash,
            ReportingCurrencyId = Guid.NewGuid(),
            Institution = "Big Bank",
            AccountNumber = "9999995678",
            IsSystem = false,
            IsArchived = false,
        });

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(before.Diff(after)));
        Assert.Equal("AccountNumber", change.Field);
        Assert.Equal("••••7890", change.OldValue);
        Assert.Equal("••••5678", change.NewValue);
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
    public void Diff_DetectsInstitutionChange()
    {
        var before = AccountSnapshot.From(CreateAccount());
        var after = before with { Institution = "New Bank" };

        var changes = before.Diff(after);

        var change = Assert.IsType<ScalarFieldChange>(Assert.Single(changes));
        Assert.Equal("Institution", change.Field);
        Assert.Equal("Big Bank", change.OldValue);
        Assert.Equal("New Bank", change.NewValue);
    }
}
