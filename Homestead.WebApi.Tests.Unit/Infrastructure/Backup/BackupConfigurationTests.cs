using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup;

public class BackupConfigurationTests
{
    private static FileStorageOptions Storage(string root) => new()
    {
        MediaPath = Path.Combine(root, "media"),
        CachePath = Path.Combine(root, "cache"),
    };

    private static readonly FileStorageOptions DefaultStorage = Storage("/srv/homestead");

    [Fact]
    public void ValidLocalTarget_HasNoError()
    {
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/mnt/nas/homestead-backup",
        };

        Assert.Null(BackupConfiguration.Validate("nas", options, DefaultStorage));
    }

    // No ValidateOnStart anywhere in this design: a typo must surface as "unavailable, because <reason>"
    // on the admin page, never as a host that refuses to boot.
    [Fact]
    public void MissingKind_YieldsAReasonRatherThanThrowing()
    {
        var options = new BackupTargetOptions { Path = "/mnt/nas/backup" };

        var error = BackupConfiguration.Validate("nas", options, DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("Kind is required", error);
    }

    [Fact]
    public void UnknownKind_YieldsAReasonRatherThanThrowing()
    {
        var options = new BackupTargetOptions
        {
            Kind = (BackupTargetKind)999,
            Path = "/mnt/nas/backup",
        };

        var error = BackupConfiguration.Validate("nas", options, DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("Unknown target kind", error);
    }

    [Fact]
    public void BlankPath_YieldsAReason()
    {
        var options = new BackupTargetOptions { Kind = BackupTargetKind.Local, Path = "   " };

        var error = BackupConfiguration.Validate("nas", options, DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("Path is required", error);
    }

    [Fact]
    public void NullPath_YieldsAReason()
    {
        var options = new BackupTargetOptions { Kind = BackupTargetKind.Local };

        var error = BackupConfiguration.Validate("nas", options, DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("Path is required", error);
    }

    // Nested inside the media root, every run copies the library into itself and the next run mirrors
    // those copies too.
    [Fact]
    public void PathInsideMediaPath_IsRefused()
    {
        var storage = Storage("/srv/homestead");
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/srv/homestead/media/backup",
        };

        var error = BackupConfiguration.Validate("nas", options, storage);

        Assert.NotNull(error);
        Assert.Contains("MediaPath", error);
    }

    [Fact]
    public void PathEqualToMediaPath_IsRefused()
    {
        var storage = Storage("/srv/homestead");
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/srv/homestead/media",
        };

        Assert.NotNull(BackupConfiguration.Validate("nas", options, storage));
    }

    [Fact]
    public void PathInsideCachePath_IsRefused()
    {
        var storage = Storage("/srv/homestead");
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/srv/homestead/cache/backup",
        };

        var error = BackupConfiguration.Validate("nas", options, storage);

        Assert.NotNull(error);
        Assert.Contains("CachePath", error);
    }

    // The strictly more dangerous direction: a mirror wrapped AROUND live storage points its own delete
    // pass at the originals.
    [Fact]
    public void PathContainingMediaPath_IsRefused()
    {
        var storage = Storage("/srv/homestead");
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/srv/homestead",
        };

        Assert.NotNull(BackupConfiguration.Validate("nas", options, storage));
    }

    // A sibling whose name merely starts with the media path's must not be mistaken for a child.
    [Fact]
    public void SiblingWithASharedNamePrefix_IsAllowed()
    {
        var storage = Storage("/srv/homestead");
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/srv/homestead/media-backup",
        };

        Assert.Null(BackupConfiguration.Validate("nas", options, storage));
    }

    [Fact]
    public void TrailingSeparatorDoesNotDefeatTheOverlapCheck()
    {
        var storage = Storage("/srv/homestead");
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.Local,
            Path = "/srv/homestead/media/",
        };

        Assert.NotNull(BackupConfiguration.Validate("nas", options, storage));
    }

    // --- GoogleDrive: the root folder path --------------------------------------------------------------

    private static BackupTargetOptions Drive(string? rootFolderPath) => new()
    {
        Kind = BackupTargetKind.GoogleDrive,
        ClientId = "client-id",
        ClientSecret = "client-secret",
        RootFolderPath = rootFolderPath,
    };

    [Theory]
    [InlineData(@"Homestead\Backup")]
    [InlineData("/Homestead/Backup")]
    [InlineData("Homestead/Backup/")]
    [InlineData("Homestead//Backup")]
    [InlineData("Homestead/   /Backup")]
    [InlineData("Homestead/./Backup")]
    [InlineData("Homestead/../Backup")]
    [InlineData("..")]
    public void AMalformedRootFolderPath_YieldsAReasonRatherThanThrowing(string path)
    {
        var error = BackupConfiguration.Validate("gdrive", Drive(path), DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("RootFolderPath", error);
    }

    // 255 matches Drive's own per-name limit, which Capabilities.MaxSegmentLength also declares.
    [Fact]
    public void ASegmentOverTheDriveNameLimit_IsRefused()
    {
        var error = BackupConfiguration.Validate(
            "gdrive", Drive("Homestead/" + new string('a', 256)), DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("255 bytes or fewer", error);
    }

    // Counted in UTF-8 bytes, not characters — Drive's limit is a byte limit, and a name of multi-byte
    // characters hits it at well under 255 chars.
    [Fact]
    public void ASegmentUnder255CharsButOver255Bytes_IsRefused()
    {
        var error = BackupConfiguration.Validate(
            "gdrive", Drive(new string('é', 200)), DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("255 bytes or fewer", error);
    }

    [Fact]
    public void ARootFolderPathOverTheColumnLength_IsRefused()
    {
        var deep = string.Join('/', Enumerable.Repeat(new string('a', 100), 12));

        var error = BackupConfiguration.Validate("gdrive", Drive(deep), DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("1024 characters or fewer", error);
    }

    // Blank means "use the default", which is by construction valid — skipping the checks entirely is
    // what keeps an unset option from reading as a misconfiguration.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRootFolderPath_FallsBackAndIsValid(string? path)
    {
        Assert.Null(BackupConfiguration.Validate("gdrive", Drive(path), DefaultStorage));
    }

    [Fact]
    public void AValidTwoSegmentPath_HasNoError()
    {
        Assert.Null(BackupConfiguration.Validate("gdrive", Drive("Homestead/Backup"), DefaultStorage));
    }

    [Fact]
    public void ASingleSegmentPath_HasNoError()
    {
        Assert.Null(BackupConfiguration.Validate("gdrive", Drive("Mirror"), DefaultStorage));
    }

    // The credential checks come first: a target with neither is misconfigured for a reason the admin can
    // act on, and naming the path instead would send them to the wrong setting.
    [Fact]
    public void MissingCredentials_AreReportedBeforeTheRootFolderPath()
    {
        var options = new BackupTargetOptions
        {
            Kind = BackupTargetKind.GoogleDrive,
            RootFolderPath = "/nonsense/",
        };

        var error = BackupConfiguration.Validate("gdrive", options, DefaultStorage);

        Assert.NotNull(error);
        Assert.Contains("ClientId is required", error);
    }

    [Fact]
    public void ReadTargets_BindsEveryConfiguredInstance()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backup:Targets:nas:Kind"] = "Local",
                ["Backup:Targets:nas:Path"] = "/mnt/nas/backup",
                ["Backup:Targets:usb:Kind"] = "Local",
                ["Backup:Targets:usb:Path"] = "/mnt/usb/backup",
            })
            .Build();

        var targets = BackupConfiguration.ReadTargets(configuration);

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.Key == "nas" && t.Options.Path == "/mnt/nas/backup");
        Assert.Contains(targets, t => t.Key == "usb" && t.Options.Path == "/mnt/usb/backup");
    }

    // Zero children = zero targets = backup off, the graceful no-op. There is no built-in destination to
    // seed (contrast the OCR engines' built-in tesseract).
    [Fact]
    public void NoConfiguredTargets_ReadsAsEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Empty(BackupConfiguration.ReadTargets(configuration));
    }
}
