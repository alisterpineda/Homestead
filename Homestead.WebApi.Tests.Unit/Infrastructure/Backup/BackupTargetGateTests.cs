using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup;

public class BackupTargetGateTests
{
    private const string TargetKey = "gdrive";

    private readonly IGoogleDriveGrantStore _store = Substitute.For<IGoogleDriveGrantStore>();

    private BackupTargetGate Sut => new(_store);

    private static IBackupTarget Target(BackupTargetKind? kind, string? configurationError = null)
    {
        var target = Substitute.For<IBackupTarget>();
        target.Key.Returns(TargetKey);
        target.Kind.Returns(kind);
        target.ConfigurationError.Returns(configurationError);
        target.IsAvailable.Returns(configurationError is null);
        return target;
    }

    private Task<TargetGateResult> CheckAsync(IBackupTarget target) =>
        Sut.CheckAsync(target, TestContext.Current.CancellationToken);

    // A local target's availability IS its config verdict — the gate has to be a pass-through for it,
    // or replacing the five call sites would change behaviour for the kind that never needed it.
    [Fact]
    public async Task ALocalTarget_PassesItsConfigVerdictStraightThrough()
    {
        Assert.True((await CheckAsync(Target(BackupTargetKind.Local))).IsAvailable);

        var broken = await CheckAsync(Target(BackupTargetKind.Local, "Path is required."));
        Assert.False(broken.IsAvailable);
        Assert.Equal("Path is required.", broken.Reason);
    }

    [Fact]
    public async Task ALocalTarget_IsNeverAskedAboutAGrant()
    {
        await CheckAsync(Target(BackupTargetKind.Local));

        await _store.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Config first, always: a Drive target with a missing client id has nothing to check in the database,
    // and "not connected" would send the admin to the wrong fix.
    [Fact]
    public async Task AConfigErrorWins_EvenOnADriveTarget()
    {
        var result = await CheckAsync(
            Target(BackupTargetKind.GoogleDrive, "ClientId is required for a GoogleDrive target."));

        Assert.False(result.IsAvailable);
        Assert.Contains("ClientId is required", result.Reason);
        await _store.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PerfectConfigButNoGrant_IsUnavailableAndSaysWhereToGo()
    {
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(GrantReadStatus.NotConnected));

        var result = await CheckAsync(Target(BackupTargetKind.GoogleDrive));

        Assert.False(result.IsAvailable);
        Assert.Equal(BackupTargetGate.NotConnectedReason, result.Reason);
    }

    [Fact]
    public async Task ADecryptFailure_ReportsItsOwnCauseRatherThanARevocation()
    {
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(GrantReadStatus.DecryptFailed));

        var result = await CheckAsync(Target(BackupTargetKind.GoogleDrive));

        Assert.False(result.IsAvailable);
        Assert.Contains("data-protection keys changed", result.Reason);
    }

    // The short-circuit that stops a run's worth of queued sibling batches from each discovering the same
    // dead grant one round trip at a time.
    [Fact]
    public async Task ALatchedTarget_IsUnavailableWithTheLatchedReason()
    {
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(
                GrantReadStatus.Ok,
                new GoogleTokenGrant("rt", "at", DateTimeOffset.UtcNow.AddHours(1)),
                AuthFailedAt: DateTimeOffset.UtcNow,
                AuthFailedReason: "Google revoked it — publish your consent screen."));

        var result = await CheckAsync(Target(BackupTargetKind.GoogleDrive));

        Assert.False(result.IsAvailable);
        Assert.Contains("publish your consent screen", result.Reason);
    }

    [Fact]
    public async Task AConnectedUnlatchedTarget_IsAvailable()
    {
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(
                GrantReadStatus.Ok, new GoogleTokenGrant("rt", "at", DateTimeOffset.UtcNow.AddHours(1))));

        var result = await CheckAsync(Target(BackupTargetKind.GoogleDrive));

        Assert.True(result.IsAvailable);
        Assert.Null(result.Reason);
    }
}
