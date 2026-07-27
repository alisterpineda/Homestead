using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Persistence;
using MediatR;
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
/// The old mirror is deliberately left where it is. The target is one-way and nothing enumerates it; the
/// files remain in the previous account, for its owner to delete by hand.
/// </para>
/// </remarks>
public class ConfirmBackupTargetConnectionHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IGoogleDriveGrantStore grantStore,
    IGoogleDriveConnectTokenProtector protector,
    GoogleDriveTokenManager tokenManager,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    ILogger<ConfirmBackupTargetConnectionHandler> logger)
    : IRequestHandler<ConfirmBackupTargetConnectionCommand, ConfirmBackupTargetConnectionResponse>
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

        // The rows are only half of the map: the target is a singleton holding a warm in-memory copy, and
        // a folder id from the old account resolves to nothing in the new one. Without this the ensure
        // step short-circuits on the stale cache and every subsequent run 404s against a parent that does
        // not exist, for the life of the process.
        if (target is IBackupTargetCache cache)
            cache.InvalidateCache();

        await grantStore.SaveAsync(target.Key, pending.Grant, pending.AccountEmail, ct);
        tokenManager.Invalidate(target.Key);

        // The target key is the identity here, so the audit id is derived from it the same way settings
        // rows do — audit entities are Guid-keyed and a config key is not.
        audit.TrackScalar(
            "BackupTarget",
            DeterministicGuid.FromKey(target.Key),
            "ConnectedAccountEmail",
            oldValue: null,
            newValue: pending.AccountEmail);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning(
            "Backup target {TargetKey} was reconnected to a different Google account; {Count} recorded " +
            "copies were forgotten and the next run will re-upload in full", target.Key, resetCopyCount);

        return new ConfirmBackupTargetConnectionResponse(
            target.Key, pending.AccountEmail, resetCopyCount);
    }
}
