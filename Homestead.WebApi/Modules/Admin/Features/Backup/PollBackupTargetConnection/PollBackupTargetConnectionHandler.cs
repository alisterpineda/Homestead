using Homestead.WebApi.Common;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PollBackupTargetConnection;

/// <summary>
/// Polls Google for the outcome of a device authorization, and stores the grant when it lands.
/// </summary>
/// <remarks>
/// Stateless between polls: the device code round-trips inside the sealed poll token rather than sitting
/// in a server-side session. Its target key is checked against the route, so a token minted for one
/// target cannot be replayed against another.
/// </remarks>
public class PollBackupTargetConnectionHandler(
    IBackupTargetResolver resolver,
    IBackupTargetOptionsAccessor options,
    IGoogleOAuthClient oauth,
    IGoogleDriveAccountClient accounts,
    IGoogleDriveGrantStore grantStore,
    IGoogleDriveConnectTokenProtector protector,
    GoogleDriveTokenManager tokenManager,
    TimeProvider timeProvider,
    ILogger<PollBackupTargetConnectionHandler> logger)
    : IRequestHandler<PollBackupTargetConnectionCommand, PollBackupTargetConnectionResponse>
{
    /// <summary>How long the admin has to answer the account-mismatch prompt.</summary>
    private static readonly TimeSpan PendingGrantLifetime = TimeSpan.FromMinutes(10);

    public async Task<PollBackupTargetConnectionResponse> Handle(
        PollBackupTargetConnectionCommand request, CancellationToken ct)
    {
        var target = resolver.Find(request.TargetKey)
            ?? throw new EntityNotFoundException($"Backup target '{request.TargetKey}' is not configured.");

        if (target.Kind is not BackupTargetKind.GoogleDrive)
            throw new BusinessRuleException($"Backup target '{target.Key}' is not a Google Drive target.");

        var pollToken = protector.UnprotectPoll(request.PollToken)
            ?? throw new BusinessRuleException(
                "This connection attempt is no longer valid. Start over from Connect.");

        if (!string.Equals(pollToken.TargetKey, target.Key, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("This connection attempt belongs to a different backup target.");

        var bag = options.Get(target.Key);

        var result = await oauth.PollDeviceTokenAsync(
            bag.ClientId!, bag.ClientSecret!, pollToken.DeviceCode, ct);

        if (result.Status is not GoogleDevicePollStatus.Granted)
            return new PollBackupTargetConnectionResponse { Status = Map(result.Status) };

        var grant = result.Grant!;

        // Whose account was that? Asked before storing, because the answer decides whether storing is
        // even the right move.
        var account = await accounts.GetAccountInfoAsync(grant.AccessToken, ct);

        var existing = await grantStore.ReadAsync(target.Key, ct);
        var existingEmail = existing.ConnectedAccountEmail;

        if (existingEmail is { Length: > 0 }
            && account.EmailAddress is { Length: > 0 }
            && !string.Equals(existingEmail, account.EmailAddress, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Backup target {TargetKey} was offered a different Google account than the one connected; " +
                "holding the grant pending confirmation", target.Key);

            return new PollBackupTargetConnectionResponse
            {
                Status = BackupConnectStatus.AccountMismatch,
                ConnectedAccountEmail = account.EmailAddress,
                ExistingAccountEmail = existingEmail,
                PendingToken = protector.ProtectPending(new GoogleConnectPendingGrant(
                    target.Key,
                    grant,
                    account.EmailAddress,
                    timeProvider.GetUtcNow().Add(PendingGrantLifetime))),
            };
        }

        // Falls back to the email already on file when Drive declines to name the account: overwriting a
        // known address with null would lose the only answer to "which Google account is this?" on a
        // reconnect that is, as far as anything here can tell, the same one.
        await grantStore.SaveAsync(target.Key, grant, account.EmailAddress ?? existingEmail, ct);

        // A cached token from a previous grant would otherwise keep writing into the old account until it
        // expired on its own.
        tokenManager.Invalidate(target.Key);

        return new PollBackupTargetConnectionResponse
        {
            Status = BackupConnectStatus.Connected,
            ConnectedAccountEmail = account.EmailAddress ?? existingEmail,
        };
    }

    private static BackupConnectStatus Map(GoogleDevicePollStatus status) => status switch
    {
        GoogleDevicePollStatus.Pending => BackupConnectStatus.Pending,
        GoogleDevicePollStatus.SlowDown => BackupConnectStatus.SlowDown,
        GoogleDevicePollStatus.Expired => BackupConnectStatus.Expired,
        GoogleDevicePollStatus.Denied => BackupConnectStatus.Denied,
        _ => BackupConnectStatus.Pending
    };
}
