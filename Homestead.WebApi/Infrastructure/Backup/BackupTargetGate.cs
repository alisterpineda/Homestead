using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

namespace Homestead.WebApi.Infrastructure.Backup;

/// <inheritdoc cref="IBackupTargetGate"/>
public class BackupTargetGate(IGoogleDriveGrantStore grantStore) : IBackupTargetGate
{
    public const string NotConnectedReason =
        "Not connected to Google — use Connect on the backup page.";

    public async Task<TargetGateResult> CheckAsync(IBackupTarget target, CancellationToken ct)
    {
        // Config first, always. A target with a missing client id has nothing to check in the database,
        // and reporting "not connected" for it would send the admin to the wrong fix.
        if (target.ConfigurationError is { } configurationError)
            return new TargetGateResult(false, configurationError);

        if (target.Kind is not BackupTargetKind.GoogleDrive)
            return new TargetGateResult(true, null);

        var read = await grantStore.ReadAsync(target.Key, ct);

        return read.Status switch
        {
            GrantReadStatus.NotConnected => new TargetGateResult(false, NotConnectedReason),
            GrantReadStatus.DecryptFailed => new TargetGateResult(false, GoogleDriveGrantStore.DecryptFailedReason),

            // A latched target still has a perfectly readable grant — it just already knows the grant is
            // dead. This is the short-circuit that stops a run's worth of queued sibling batches from
            // discovering the same failure one round trip at a time.
            _ when read.AuthFailedAt is not null =>
                new TargetGateResult(false, read.AuthFailedReason ?? "The Google authorization failed — reconnect."),

            _ => new TargetGateResult(true, null)
        };
    }
}
