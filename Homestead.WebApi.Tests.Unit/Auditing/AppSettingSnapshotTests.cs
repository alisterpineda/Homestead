using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Tests.Unit.Auditing;

public class AppSettingSnapshotTests
{
    [Fact]
    public void From_MapsValueField()
    {
        var setting = new AppSetting
        {
            Module = "Documents",
            Value = """{"maxUploadSizeMb":200}"""
        };

        var snapshot = AppSettingSnapshot.From(setting);

        Assert.Equal("""{"maxUploadSizeMb":200}""", snapshot.Value);
    }

    [Fact]
    public void Diff_DetectsValueChange()
    {
        var before = new AppSettingSnapshot { Value = """{"maxUploadSizeMb":100}""" };
        var after = new AppSettingSnapshot { Value = """{"maxUploadSizeMb":200}""" };

        var changes = before.Diff(after);

        var change = Assert.Single(changes);
        var scalar = Assert.IsType<ScalarFieldChange>(change);
        Assert.Equal("Value", scalar.Field);
        Assert.Equal("""{"maxUploadSizeMb":100}""", scalar.OldValue);
        Assert.Equal("""{"maxUploadSizeMb":200}""", scalar.NewValue);
    }

    [Fact]
    public void Diff_ReturnsEmpty_WhenIdentical()
    {
        var snapshot = new AppSettingSnapshot { Value = """{"maxUploadSizeMb":100}""" };

        var changes = snapshot.Diff(snapshot);

        Assert.Empty(changes);
    }

    [Fact]
    public void ModuleToGuid_ReturnsDeterministicGuid()
    {
        var guid1 = AppSettingSnapshot.ModuleToGuid("Documents");
        var guid2 = AppSettingSnapshot.ModuleToGuid("Documents");

        Assert.Equal(guid1, guid2);
    }

    [Fact]
    public void ModuleToGuid_DifferentModules_ReturnDifferentGuids()
    {
        var documentsGuid = AppSettingSnapshot.ModuleToGuid("Documents");
        var systemGuid = AppSettingSnapshot.ModuleToGuid("System");

        Assert.NotEqual(documentsGuid, systemGuid);
    }
}
