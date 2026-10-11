using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DisconnectBackupTarget;

/// <summary>
/// Revokes and forgets a target's stored Google authorization.
/// </summary>
/// <remarks>
/// <para>
/// <b>The revocation call is not optional politeness.</b> Google does not invalidate old refresh tokens
/// when issuing new ones, so a disconnect that only deleted the local row would strand a live credential
/// in the user's Google security page forever. Best-effort, though: a revocation we cannot deliver must
/// never block the disconnect itself.
/// </para>
/// <para>
/// Resolves through the DATABASE rather than through config, so a target whose config key has since
/// vanished can still be disconnected. Its card renders from the surviving state row precisely so this
/// stays reachable — an orphaned grant with no surface is the failure mode this guards against.
/// </para>
/// </remarks>
public class DisconnectBackupTargetHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IGoogleDriveGrantStore grantStore,
    IGoogleOAuthClient oauth,
    GoogleDriveTokenManager tokenManager,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
{
    public async Task<bool> HandleAsync(string key, CancellationToken ct)
    {
        // The state row is the authority on "is there something to disconnect", and it outlives config.
        key = key.Trim();

        var state = await db.BackupTargetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TargetKey == key, ct)
            ?? throw new EntityNotFoundException($"Backup target '{key}' has nothing to disconnect.");

        var target = resolver.Find(key);

        var email = state.ConnectedAccountEmail;

        // Read before clearing: revocation needs the refresh token, and a grant that will not decrypt is
        // still one to forget locally.
        var read = await grantStore.ReadAsync(key, ct);

        if (read.Grant is { } grant)
            await oauth.RevokeAsync(grant.RefreshToken, ct);

        await grantStore.ClearAsync(key, ct);
        tokenManager.Invalidate(key);

        // The folder map goes with the grant — its ids are only meaningful under the authorization that
        // created them. State rows deliberately stay: re-connecting the SAME account should not force a
        // full re-upload, and reconnecting a different one resets them explicitly (see the confirm slice).
        //
        // For the same reason the state row's RootFolderPath is deliberately NOT cleared: it is the pin
        // that keeps those surviving copy records coherent. Clearing it would let a config edit between
        // disconnect and reconnect create a new empty root while every mirrored file stayed in the old
        // tree. Reconnecting the same account therefore re-finds the same folders by name along the pinned
        // path, oldest-wins. RootFolderWebViewLink stays too, so the "the files stay in that account's
        // Drive — delete them there if you want them gone" advice this dialog gives is one click away.
        //
        // Through the change tracker rather than ExecuteDelete: the map is bounded by the number of
        // folders (roughly, year shards — tens), so there is no bulk win to buy with a raw write.
        var folders = await db.BackupTargetFolders
            .Where(f => f.TargetKey == key)
            .ToListAsync(ct);

        db.BackupTargetFolders.RemoveRange(folders);

        // The rows are only half of the map — the target is a singleton with a warm in-memory copy, and
        // leaving it populated would keep every run resolving ids that no longer belong to any grant.
        if (target is IBackupTargetCache cache)
            cache.InvalidateCache();

        audit.TrackScalar(
            "BackupTarget",
            DeterministicGuid.FromKey(target?.Key ?? key),
            "ConnectedAccountEmail",
            oldValue: email,
            newValue: null);

        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
