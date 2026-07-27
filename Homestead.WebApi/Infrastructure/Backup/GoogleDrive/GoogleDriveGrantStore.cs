using System.Security.Cryptography;
using System.Text.Json;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <inheritdoc cref="IGoogleDriveGrantStore"/>
public class GoogleDriveGrantStore(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<GoogleDriveGrantStore> logger) : IGoogleDriveGrantStore
{
    /// <summary>
    /// The protector purpose. One purpose for every target rather than one per key: the target key is
    /// already part of the row the payload sits on, and a per-key purpose would break every stored grant
    /// the moment someone renamed a config section.
    /// </summary>
    public const string ProtectorPurpose = "Homestead.Backup.GoogleDrive";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public async Task<GrantReadResult> ReadAsync(string targetKey, CancellationToken ct)
    {
        var state = await db.BackupTargetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TargetKey == targetKey, ct);

        if (state?.EncryptedGrant is not { Length: > 0 } ciphertext)
            return new GrantReadResult(GrantReadStatus.NotConnected);

        GoogleTokenGrant? grant;
        try
        {
            grant = JsonSerializer.Deserialize<GoogleTokenGrant>(_protector.Unprotect(ciphertext));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Never logs the ciphertext, and never falls through to "not connected" — the distinction is
            // the entire reason DecryptFailed exists as a separate status.
            logger.LogWarning(
                "Stored Google grant for backup target {TargetKey} could not be decrypted", targetKey);

            return new GrantReadResult(
                GrantReadStatus.DecryptFailed,
                ConnectedAccountEmail: state.ConnectedAccountEmail,
                GrantIssuedAt: state.GrantIssuedAt,
                AuthFailedAt: state.AuthFailedAt,
                AuthFailedReason: DecryptFailedReason);
        }

        if (grant is null)
            return new GrantReadResult(
                GrantReadStatus.DecryptFailed,
                ConnectedAccountEmail: state.ConnectedAccountEmail,
                AuthFailedReason: DecryptFailedReason);

        return new GrantReadResult(
            GrantReadStatus.Ok,
            grant,
            state.ConnectedAccountEmail,
            state.GrantIssuedAt,
            state.AuthFailedAt,
            state.AuthFailedReason);
    }

    public const string DecryptFailedReason =
        "The stored Google credential could not be decrypted (the data-protection keys changed) — reconnect.";

    public async Task SaveAsync(
        string targetKey, GoogleTokenGrant grant, string? connectedAccountEmail, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var state = await GetOrCreateAsync(targetKey, now, ct);

        state.EncryptedGrant = Protect(grant);
        state.ConnectedAccountEmail = connectedAccountEmail;
        state.GrantIssuedAt = now;

        // A fresh authorization is exactly the thing the latch was waiting for.
        state.AuthFailedAt = null;
        state.AuthFailedReason = null;
        state.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateTokensAsync(string targetKey, GoogleTokenGrant grant, CancellationToken ct)
    {
        var state = await db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == targetKey, ct);

        // Disconnected mid-refresh. Writing the rotated token back would silently reconnect a target the
        // operator just detached.
        if (state?.EncryptedGrant is null)
            return;

        state.EncryptedGrant = Protect(grant);
        state.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(string targetKey, CancellationToken ct)
    {
        var state = await db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == targetKey, ct);
        if (state is null)
            return;

        state.EncryptedGrant = null;
        state.ConnectedAccountEmail = null;
        state.GrantIssuedAt = null;
        state.AuthFailedAt = null;
        state.AuthFailedReason = null;
        state.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    public async Task LatchAuthFailureAsync(
        string targetKey, string errorCode, string message, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var state = await GetOrCreateAsync(targetKey, now, ct);

        // First writer wins. A run's worth of sibling batches can all discover the same dead grant within
        // seconds of each other; re-stamping the time on each would keep pushing the "since when" forward
        // and make the failure look like it just started every time anyone looked.
        if (state.AuthFailedAt is not null)
            return;

        state.AuthFailedAt = now;
        state.AuthFailedReason = BuildReason(errorCode, message, state.GrantIssuedAt, now);
        state.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Backup target {TargetKey} latched as disconnected from Google (OAuth error {ErrorCode})",
            targetKey, errorCode);
    }

    public async Task ClearAuthFailureAsync(string targetKey, CancellationToken ct)
    {
        var state = await db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == targetKey, ct);
        if (state?.AuthFailedAt is null)
            return;

        state.AuthFailedAt = null;
        state.AuthFailedReason = null;
        state.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Turns an OAuth error code into the sentence that actually helps.
    /// </summary>
    /// <remarks>
    /// The seven-day window is the case worth special-casing. A consent screen left in "Testing" status
    /// auto-revokes its refresh tokens after seven days, which for a scheduled backup reads as a silent
    /// weekly death with a generic "reconnect" that works — for another week. Naming the cause turns an
    /// infinite loop of reconnects into one fix. The window is 6–9 days rather than exactly 7 because
    /// the revocation lands whenever the next run happens to notice it.
    /// </remarks>
    internal static string BuildReason(
        string errorCode, string message, DateTimeOffset? grantIssuedAt, DateTimeOffset now)
    {
        if (errorCode != "invalid_grant" || grantIssuedAt is not { } issuedAt)
            return message;

        var age = now - issuedAt;

        if (age < TimeSpan.FromDays(6) || age > TimeSpan.FromDays(9))
            return message;

        return
            "Google revoked the authorization about seven days after it was issued, which almost always " +
            "means the OAuth consent screen is still in Testing mode. Publish it to \"In production\" " +
            "(no verification review is needed for the drive.file scope), then reconnect.";
    }

    private async Task<BackupTargetState> GetOrCreateAsync(
        string targetKey, DateTimeOffset now, CancellationToken ct)
    {
        var state = await db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == targetKey, ct);

        if (state is not null)
            return state;

        // Lazily, exactly as PatchBackupTargetHandler does: a target that has never been touched has no
        // row, and connecting one is the first touch.
        state = new BackupTargetState
        {
            TargetKey = targetKey,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.BackupTargetStates.Add(state);
        return state;
    }

    private string Protect(GoogleTokenGrant grant) =>
        _protector.Protect(JsonSerializer.Serialize(grant));
}
