using Homestead.WebApi.Common;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.ValidateBackupTargetConnection;

/// <summary>
/// Proves — or disproves — that a target's stored authorization still works, and reports its free space.
/// </summary>
/// <remarks>
/// <para>
/// The only endpoint on the read path that touches Google. <c>GET /targets</c> stays a pure config+DB
/// read so the page renders instantly; the frontend then fires this per connected Drive target and
/// corrects the badge in place. That split is what keeps lazy detection honest without making every page
/// load wait on a third party.
/// </para>
/// <para>
/// It latches in both directions. A failed probe writes the auth-failed marker (via the token manager),
/// so the next scheduled run refuses immediately instead of discovering the same thing the hard way; a
/// successful one clears a stale marker, which is what makes "reconnect, then look again" work.
/// </para>
/// </remarks>
public class ValidateBackupTargetConnectionHandler(
    IBackupTargetResolver resolver,
    IBackupTargetOptionsAccessor options,
    IBackupTargetGate gate,
    IGoogleDriveGrantStore grantStore,
    IGoogleDriveAccountClient accounts,
    GoogleDriveTokenManager tokenManager,
    ILogger<ValidateBackupTargetConnectionHandler> logger)
    : IRequestHandler<ValidateBackupTargetConnectionCommand, ValidateBackupTargetConnectionResponse>
{
    public async Task<ValidateBackupTargetConnectionResponse> Handle(
        ValidateBackupTargetConnectionCommand request, CancellationToken ct)
    {
        var target = resolver.Find(request.TargetKey)
            ?? throw new EntityNotFoundException($"Backup target '{request.TargetKey}' is not configured.");

        // Nothing to probe on a kind with no credential; report the cheap composed answer unchanged.
        if (target.Kind is not BackupTargetKind.GoogleDrive)
        {
            var localVerdict = await gate.CheckAsync(target, ct);
            return new ValidateBackupTargetConnectionResponse
            {
                IsAvailable = localVerdict.IsAvailable,
                Reason = localVerdict.Reason,
            };
        }

        // Config and "is a grant even stored" first — the probe below has nothing to prove otherwise, and
        // a round trip to Google would only produce a worse-worded version of the same answer.
        var verdict = await gate.CheckAsync(target, ct);
        var read = await grantStore.ReadAsync(target.Key, ct);

        if (read.Status is not GrantReadStatus.Ok)
            return new ValidateBackupTargetConnectionResponse
            {
                IsAvailable = false,
                Reason = verdict.Reason,
                ConnectedAccountEmail = read.ConnectedAccountEmail,
            };

        var bag = options.Get(target.Key);

        try
        {
            // Goes through the token manager rather than straight to the refresh endpoint, so a valid
            // cached token is not spent needlessly — and so a failure latches exactly once, in the one
            // place that owns the latch.
            var accessToken = await tokenManager.GetAccessTokenAsync(
                target.Key, bag.ClientId!, bag.ClientSecret!, ct);

            var account = await accounts.GetAccountInfoAsync(accessToken, ct);

            // The probe succeeded, so any marker left by an earlier failure is stale.
            await grantStore.ClearAuthFailureAsync(target.Key, ct);

            return new ValidateBackupTargetConnectionResponse
            {
                IsAvailable = true,
                ConnectedAccountEmail = account.EmailAddress ?? read.ConnectedAccountEmail,
                StorageUsedBytes = account.StorageUsedBytes,
                StorageLimitBytes = account.StorageLimitBytes,
            };
        }
        catch (GoogleDriveAuthException ex)
        {
            // Already latched by the token manager for a refresh failure; latched here for the narrower
            // case of a token that refreshed fine and was then rejected by Drive itself.
            await grantStore.LatchAuthFailureAsync(target.Key, ex.ErrorCode, ex.Message, ct);

            var latched = await grantStore.ReadAsync(target.Key, ct);

            return new ValidateBackupTargetConnectionResponse
            {
                IsAvailable = false,
                Reason = latched.AuthFailedReason ?? ex.Message,
                ConnectedAccountEmail = read.ConnectedAccountEmail,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A network blip is NOT a dead grant, and must not latch — the whole point of separating the
            // two exception types. Report it as unknown-for-now and leave the stored state alone.
            logger.LogInformation(ex,
                "Could not reach Google to validate backup target {TargetKey}", target.Key);

            return new ValidateBackupTargetConnectionResponse
            {
                IsAvailable = verdict.IsAvailable,
                Reason = "Could not reach Google just now — the stored connection was left unchanged.",
                ConnectedAccountEmail = read.ConnectedAccountEmail,
            };
        }
    }
}
