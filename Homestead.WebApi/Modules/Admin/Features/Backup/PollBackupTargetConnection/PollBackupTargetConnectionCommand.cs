using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PollBackupTargetConnection;

public record PollBackupTargetConnectionCommand(string TargetKey, string PollToken)
    : ICommand<PollBackupTargetConnectionResponse>;

public enum BackupConnectStatus
{
    /// <summary>The admin has not finished consenting. Poll again at the stated interval.</summary>
    Pending,

    /// <summary>Polling too fast; back off before the next attempt.</summary>
    SlowDown,

    /// <summary>Stored. The target is connected.</summary>
    Connected,

    /// <summary>The code aged out. Start over.</summary>
    Expired,

    /// <summary>The admin (or a Workspace policy) refused.</summary>
    Denied,

    /// <summary>
    /// Consent landed, but for a <i>different</i> Google account than the one already connected. Nothing
    /// was stored — stored file ids are meaningless in another account's Drive, so switching abandons the
    /// existing mirror and forces a full re-upload, which is too destructive to do implicitly.
    /// </summary>
    AccountMismatch
}

public record PollBackupTargetConnectionResponse
{
    public required BackupConnectStatus Status { get; init; }

    /// <summary>The account that just consented (or, on success, the one now stored).</summary>
    public string? ConnectedAccountEmail { get; init; }

    /// <summary>On <see cref="BackupConnectStatus.AccountMismatch"/>, the account already on file.</summary>
    public string? ExistingAccountEmail { get; init; }

    /// <summary>
    /// On <see cref="BackupConnectStatus.AccountMismatch"/>, the sealed grant to hand to the confirm
    /// endpoint. Held rather than stored so the admin gets to decide, and encrypted because it is a live
    /// credential in transit.
    /// </summary>
    public string? PendingToken { get; init; }
}
