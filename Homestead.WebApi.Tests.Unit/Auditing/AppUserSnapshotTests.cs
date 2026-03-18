using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class AppUserSnapshotTests
{
    [Fact]
    public void From_WithoutRole_MapsFields()
    {
        var user = new AppUser
        {
            Email = "test@example.com",
            DisplayName = "Test User",
            IsActive = true
        };

        var snapshot = AppUserSnapshot.From(user);

        Assert.Equal("test@example.com", snapshot.Email);
        Assert.Equal("Test User", snapshot.DisplayName);
        Assert.True(snapshot.IsActive);
        Assert.Null(snapshot.Role);
    }

    [Fact]
    public void From_WithRole_MapsAllFields()
    {
        var user = new AppUser
        {
            Email = "admin@example.com",
            DisplayName = "Admin",
            IsActive = true
        };

        var snapshot = AppUserSnapshot.From(user, "Admin");

        Assert.Equal("admin@example.com", snapshot.Email);
        Assert.Equal("Admin", snapshot.DisplayName);
        Assert.True(snapshot.IsActive);
        Assert.Equal("Admin", snapshot.Role);
    }

    [Fact]
    public void Diff_DetectsEmailChange()
    {
        var before = new AppUserSnapshot { DisplayName = "User", Email = "old@test.com", IsActive = true, Role = "Admin" };
        var after = new AppUserSnapshot { DisplayName = "User", Email = "new@test.com", IsActive = true, Role = "Admin" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Email", scalar.Field);
    }

    [Fact]
    public void Diff_DetectsDisplayNameChange()
    {
        var before = new AppUserSnapshot { DisplayName = "Old Name", Email = "e@t.com", IsActive = true, Role = "Admin" };
        var after = new AppUserSnapshot { DisplayName = "New Name", Email = "e@t.com", IsActive = true, Role = "Admin" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("DisplayName", scalar.Field);
    }

    [Fact]
    public void Diff_DetectsIsActiveChange()
    {
        var before = new AppUserSnapshot { DisplayName = "User", Email = "e@t.com", IsActive = true, Role = "Admin" };
        var after = new AppUserSnapshot { DisplayName = "User", Email = "e@t.com", IsActive = false, Role = "Admin" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("IsActive", scalar.Field);
        Assert.Equal("True", scalar.OldValue);
        Assert.Equal("False", scalar.NewValue);
    }

    [Fact]
    public void Diff_DetectsRoleChange()
    {
        var before = new AppUserSnapshot { DisplayName = "User", Email = "e@t.com", IsActive = true, Role = "Admin" };
        var after = new AppUserSnapshot { DisplayName = "User", Email = "e@t.com", IsActive = true, Role = "Viewer" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Role", scalar.Field);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = new AppUserSnapshot { DisplayName = "User", Email = "e@t.com", IsActive = true, Role = "Admin" };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }
}
