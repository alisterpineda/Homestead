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
            Key = "documents",
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
    public void KeyToGuid_ReturnsDeterministicGuid()
    {
        var guid1 = AppSettingSnapshot.KeyToGuid("documents");
        var guid2 = AppSettingSnapshot.KeyToGuid("documents");

        Assert.Equal(guid1, guid2);
    }

    [Fact]
    public void KeyToGuid_DifferentKeys_ReturnDifferentGuids()
    {
        var documentsGuid = AppSettingSnapshot.KeyToGuid("documents");
        var systemGuid = AppSettingSnapshot.KeyToGuid("system");

        Assert.NotEqual(documentsGuid, systemGuid);
    }
}
