using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConfirmBackupTargetConnection;

/// <summary>
/// Finishes an account switch: stores the held grant and forgets everything that described the old
/// account's mirror.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reset is the point, not a side effect.</b> A Drive file id means nothing in another account's
/// Drive, so every <c>DocumentBackupState</c> handle, every parked upload session, and every folder-map
/// entry is now a pointer into a Drive this grant cannot see. Keeping them would make the next run patch
/// ids that do not resolve and skip copies it believes are already mirrored — a mirror that reports
/// converged and is empty. Dropping them makes the next run a clean full upload, which is the only
/// correct outcome.
/// </para>
/// <para>
/// That includes the <b>pinned root path</b>, which this is the only path allowed to clear. Everywhere
/// else the pin keeps surviving copy records coherent with where their files physically are; here there
/// are no surviving copy records, so the new account's root is picked from config afresh and provisioned
/// immediately.
/// </para>
/// <para>
/// The old mirror is deliberately left where it is. The target is one-way and nothing enumerates it; the
/// files remain in the previous account, for its owner to delete by hand.
/// </para>
/// <para>
/// <b>The authorization we walk away from is handed back.</b> Google's <c>/revoke</c> is grant-scoped
/// rather than token-scoped — it drops every token this client holds for that account — which is exactly
/// why this is one of the few places it is safe to call: the account we are leaving is by definition not
/// the account we just stored, so this target has nothing left on that grant to take out.
/// </para>
/// <para>
/// <b>This target.</b> A grant is per account-plus-client while the switch is per target, so a second Drive
/// target sharing this OAuth client and still connected to the account being left behind loses its grant
/// too. Pre-existing in shape (<c>DisconnectBackupTargetHandler</c> revokes unconditionally and has always
/// had this), recoverable by reconnecting that target, and not narrowed here: config owns the client id and
/// nothing in the schema records which grant a token belongs to.
/// </para>
/// </remarks>
public class ConfirmBackupTargetConnectionHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IGoogleDriveGrantStore grantStore,
    IGoogleOAuthClient oauth,
    IGoogleDriveConnectTokenProtector protector,
    GoogleDriveTokenManager tokenManager,
    IBackupTargetRootProvisioner rootProvisioner,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    ILogger<ConfirmBackupTargetConnectionHandler> logger)
{
    public async Task<ConfirmBackupTargetConnectionResponse> Handle(
        ConfirmBackupTargetConnectionCommand request, CancellationToken ct)
    {
        var target = resolver.Find(request.TargetKey)
            ?? throw new EntityNotFoundException($"Backup target '{request.TargetKey}' is not configured.");

        if (target.Kind is not BackupTargetKind.GoogleDrive)
            throw new BusinessRuleException($"Backup target '{target.Key}' is not a Google Drive target.");

        var pending = protector.UnprotectPending(request.PendingToken)
            ?? throw new BusinessRuleException(
                "This connection attempt is no longer valid. Start over from Connect.");

        if (!string.Equals(pending.TargetKey, target.Key, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("This connection attempt belongs to a different backup target.");

        // Read before the store overwrites it: the outgoing refresh token is the only handle on the
        // authorization this switch abandons. A DecryptFailed read yields a null Grant, which needs no
        // special case — a token we cannot read is a token we cannot revoke either.
        var outgoing = await grantStore.ReadAsync(target.Key, ct);

        // The pairings writing to this target — the join the state rows have no column for, since they
        // key off the pairing rather than the destination.
        var sourceIds = await db.BackupSources
            .Where(s => s.TargetKey == target.Key)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var resetCopyCount = 0;

        if (sourceIds.Count > 0)
        {
            resetCopyCount = await db.DocumentBackupStates
                .Where(s => sourceIds.Contains(s.BackupSourceId))
                .ExecuteDeleteAsync(ct);

            await db.BackupUploadSessions
                .Where(s => sourceIds.Contains(s.BackupSourceId))
                .ExecuteDeleteAsync(ct);
        }

        // Through the change tracker, unlike the two above: the folder map is bounded by the number of
        // folders (roughly, year shards — tens), so there is no bulk win to buy with a raw write.
        var folders = await db.BackupTargetFolders
            .Where(f => f.TargetKey == target.Key)
            .ToListAsync(ct);

        db.BackupTargetFolders.RemoveRange(folders);

        // Clearing the pinned root is correct on THIS path and no other, because this is the same path
        // that just deleted every DocumentBackupState row: a new account means new ids, new folders and a
        // full re-upload, so there is nothing left for the old path to keep coherent. (Disconnect keeps
        // those rows and must therefore keep the pin.) The grant store shares this scoped AppDbContext, so
        // both writes flush inside the one transaction.
        var state = await db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == target.Key, ct);

        if (state is not null)
        {
            state.RootFolderPath = null;
            state.RootFolderWebViewLink = null;
        }

        // The rows are only half of the map: the target is a singleton holding a warm in-memory copy, and
        // a folder id from the old account resolves to nothing in the new one. Without this the ensure
        // step short-circuits on the stale cache and every subsequent run 404s against a parent that does
        // not exist, for the life of the process.
        if (target is IBackupTargetCache cache)
            cache.InvalidateCache();

        await grantStore.SaveAsync(target.Key, pending.Grant, pending.AccountEmail, ct);
        tokenManager.Invalidate(target.Key);

        // The new account's root, before the old account's authorization is handed back. Both hooks are
        // independent and both self-guard; the order simply reads correctly. After the commit for the same
        // reason the revoke is: the grant is not durable — nor visible to the token manager's own
        // connection — until then. CancellationToken.None for that reason too: the provisioner swallows
        // everything but cancellation, and a cancelled hook would throw out of a switch already committed.
        unitOfWork.OnAfterCommit(_ => rootProvisioner.EnsureAsync(target, CancellationToken.None));

        // After the commit, never inline. This command bulk-deletes copy records and upload sessions before
        // it gets here, so a rollback anywhere restores the old grant — and revoking inline would leave that
        // restored grant pointing at a token already killed, a dead target with nothing on the card to say
        // why.
        //
        // The token comparison is load-bearing, not defensive noise: a pending token stays valid for its
        // whole ten-minute window and carries no single-use marker, so a double-clicked "Switch account"
        // replays this command with the grant it already stored — and an unguarded revoke would kill the
        // live connection it just made. Ordinal, because these are opaque credential strings.
        if (outgoing.Grant is { } abandoned
            && !string.Equals(abandoned.RefreshToken, pending.Grant.RefreshToken, StringComparison.Ordinal))
            unitOfWork.OnAfterCommit(_ => RevokeAbandonedAsync(abandoned.RefreshToken, target.Key));

        // The target key is the identity here, so the audit id is derived from it the same way settings
        // rows do — audit entities are Guid-keyed and a config key is not.
        audit.TrackScalar(
            "BackupTarget",
            DeterministicGuid.FromKey(target.Key),
            "ConnectedAccountEmail",
            oldValue: outgoing.ConnectedAccountEmail,
            newValue: pending.AccountEmail);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning(
            "Backup target {TargetKey} was reconnected to a different Google account; {Count} recorded " +
            "copies were forgotten and the next run will re-upload in full", target.Key, resetCopyCount);

        return new ConfirmBackupTargetConnectionResponse(
            target.Key, pending.AccountEmail, resetCopyCount);
    }

    /// <summary>
    /// Hands the abandoned authorization back, without ever letting that failure read as the switch failing.
    /// </summary>
    /// <remarks>
    /// Its own try/catch on top of <see cref="IGoogleOAuthClient.RevokeAsync"/>'s, which is best-effort for
    /// transport failures only. This runs as an after-commit hook, so anything else escaping it would turn a
    /// switch that has already been committed into a 500 — reporting a failure that did not happen, for a
    /// courtesy call that cannot be retried anyway. <see cref="CancellationToken.None"/> because RevokeAsync
    /// swallows TaskCanceledException: honouring the request token would silently no-op in exactly the case
    /// that produces this path most often, the admin closing the tab.
    /// </remarks>
    private async Task RevokeAbandonedAsync(string refreshToken, string targetKey)
    {
        try
        {
            await oauth.RevokeAsync(refreshToken, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not revoke the Google authorization backup target {TargetKey} switched away from; it " +
                "may linger in that account's third-party access page", targetKey);
        }
    }
}
