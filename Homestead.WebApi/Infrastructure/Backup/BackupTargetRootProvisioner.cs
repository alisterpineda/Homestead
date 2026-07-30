namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Creates a target's root folder chain, and never lets that failure read as the caller's failing.
/// </summary>
/// <remarks>
/// <para>
/// Exists so the "best-effort, swallow everything" posture lives in one place rather than being
/// copy-pasted across the three connect-flow slices that need it. Eager creation at connect time is what
/// makes the share-the-mirror-with-someone workflow possible at all: you cannot share a folder that does
/// not exist yet, and nothing else creates the root until a first run succeeds.
/// </para>
/// <para>
/// The swallow is load-bearing at the after-commit call sites. <c>UnitOfWork.RunAfterCommitHooksAsync</c>
/// awaits hooks unguarded, so a throw here would turn a connect that <b>already committed</b> into a 500 —
/// reporting a failure that did not happen, for work the next validate repairs within a second anyway.
/// <c>ConfirmBackupTargetConnectionHandler.RevokeAbandonedAsync</c> is the existing precedent.
/// </para>
/// </remarks>
public interface IBackupTargetRootProvisioner
{
    /// <summary>
    /// Ensures the target's root exists. Idempotent, cheap on a target whose root is already mapped, and
    /// never throws for anything but cancellation.
    /// </summary>
    Task EnsureAsync(IBackupTarget target, CancellationToken ct);
}

/// <inheritdoc cref="IBackupTargetRootProvisioner"/>
public class BackupTargetRootProvisioner(ILogger<BackupTargetRootProvisioner> logger)
    : IBackupTargetRootProvisioner
{
    public async Task EnsureAsync(IBackupTarget target, CancellationToken ct)
    {
        if (!target.IsAvailable)
            return;

        try
        {
            // An empty collection ensures exactly the root chain and nothing else: the target's own
            // ancestor expansion always includes the root, and emits nothing for a set with no members.
            await target.EnsureFoldersAsync([], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Could not create the backup root folder for target {TargetKey}; it will be created on the " +
                "next validate or run", target.Key);
        }
    }
}
