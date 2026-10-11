using Homestead.WebApi.Common;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PollBackupTargetConnection;

/// <summary>
/// Polls Google for the outcome of a device authorization, and stores the grant when it lands.
/// </summary>
/// <remarks>
/// <para>
/// Stateless between polls: the device code round-trips inside the sealed poll token rather than sitting
/// in a server-side session. Its target key is checked against the route, so a token minted for one
/// target cannot be replayed against another.
/// </para>
/// <para>
/// <b>A failed first connect hands its authorization back; a failed reconnect does not.</b> Google's
/// <c>/revoke</c> is grant-scoped rather than token-scoped, so on a target that already has a grant on
/// file there is no way to drop the token we are abandoning without also dropping the one the mirror
/// runs on. See the probe's catch arms for the full rule. The same gate covers a consent approved without
/// the Drive tickbox, which the token exchange rejects before the probe is ever reached.
/// </para>
/// <para>
/// The <c>NotConnected</c> gate is per-target, while a Google grant is per account-plus-client — so two
/// Drive targets sharing one OAuth client and one Google account can still have a revoke aimed at the
/// unconnected one land on the connected one's grant. Pre-existing (<c>DisconnectBackupTargetHandler</c>
/// revokes unconditionally and has always had this), recoverable by reconnecting, and not narrowed here
/// because the probe that would have named the account is exactly what failed.
/// </para>
/// <para>
/// <b>Not every post-exchange failure is covered, and cannot be.</b> The grant is not durable when
/// <c>HandleAsync</c> returns: <c>grantStore.SaveAsync</c>'s flush is joined to the pipeline transaction by
/// <c>CommandSaveChangesInterceptor</c>, and the unit of work commits afterwards. There is
/// <c>OnAfterCommit</c> but no <c>OnRollback</c>, so a commit that fails at that point orphans a token no
/// catch inside this handler can see. A single-row upsert failing is narrow enough not to be worth new
/// machinery for — but it is a gap, not a solved case.
/// </para>
/// <para>
/// <b>This is where a first connect provisions the root folder</b> — and a same-account reconnect too.
/// (The account-switch path is the confirm slice.) Deferred to after the commit rather than run inline:
/// see the hook's own comment.
/// </para>
/// </remarks>
public class PollBackupTargetConnectionHandler(
    IBackupTargetResolver resolver,
    IBackupTargetOptionsAccessor options,
    IGoogleOAuthClient oauth,
    IGoogleDriveAccountClient accounts,
    IGoogleDriveGrantStore grantStore,
    IGoogleDriveConnectTokenProtector protector,
    GoogleDriveTokenManager tokenManager,
    IBackupTargetRootProvisioner rootProvisioner,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PollBackupTargetConnectionHandler> logger)
{
    /// <summary>How long the admin has to answer the account-mismatch prompt.</summary>
    private static readonly TimeSpan PendingGrantLifetime = TimeSpan.FromMinutes(10);

    public async Task<PollBackupTargetConnectionResponse> HandleAsync(
        string key, PollBackupTargetConnectionBody body, CancellationToken ct)
    {
        var target = resolver.Find(key)
            ?? throw new EntityNotFoundException($"Backup target '{key}' is not configured.");

        if (target.Kind is not BackupTargetKind.GoogleDrive)
            throw new BusinessRuleException($"Backup target '{target.Key}' is not a Google Drive target.");

        var pollToken = protector.UnprotectPoll(body.PollToken)
            ?? throw new BusinessRuleException(
                "This connection attempt is no longer valid. Start over from Connect.");

        if (!string.Equals(pollToken.TargetKey, target.Key, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("This connection attempt belongs to a different backup target.");

        var bag = options.Get(target.Key);

        GoogleDevicePollResult result;
        try
        {
            result = await oauth.PollDeviceTokenAsync(
                bag.ClientId!, bag.ClientSecret!, pollToken.DeviceCode, ct);
        }
        catch (GoogleDriveAuthException ex) when (ex.OrphanedToken is { Length: > 0 } orphaned)
        {
            // A consent approved without the Drive tickbox: a live authorization we will never use, and one
            // Google remembers — so leaving it in place can answer the retry this message asks for without
            // ever showing the checkbox again. Gated exactly as the probe's arm below is, and for the same
            // reason: /revoke is GRANT-scoped, so with a grant already on file this would drop the token the
            // mirror runs on. The exchange only ever mints its own token, so nothing else can reach this one.
            if ((await grantStore.ReadAsync(target.Key, ct)).Status is GrantReadStatus.NotConnected)
                await TryRevokeAsync(target.Key, orphaned);

            throw;
        }

        if (result.Status is not GoogleDevicePollStatus.Granted)
            return new PollBackupTargetConnectionResponse { Status = Map(result.Status) };

        var grant = result.Grant!;

        // Read BEFORE the probe, not after: the failure path below has to know whether a live grant already
        // exists, and the probe is the very thing that would otherwise have told us whose account this is.
        // Costs one round trip's worth of extra read-to-use window, which matters only if another admin
        // disconnects the same target concurrently.
        var existing = await grantStore.ReadAsync(target.Key, ct);
        var existingEmail = existing.ConnectedAccountEmail;

        // Whose account was that? Asked before storing, because the answer decides whether storing is
        // even the right move.
        GoogleDriveAccountInfo account;
        try
        {
            account = await accounts.GetAccountInfoAsync(grant.AccessToken, ct);
        }
        catch (GoogleDriveAuthException) when (existing.Status is GrantReadStatus.NotConnected)
        {
            // Hand back the authorization we are walking away from — nothing is stored, so Disconnect
            // (which revokes only what IS stored) could never reach it again.
            //
            // Gated on NotConnected because Google's /revoke is GRANT-scoped, not token-scoped: it drops
            // every token this client holds for that account. With a grant already on file we cannot tell
            // whether this consent belongs to the same account — the probe that would have said so is what
            // just failed — and revoking the wrong one takes out a working mirror. An extra token on a
            // grant that is already accounted for is not worth that risk.
            //
            // Only this exception type revokes, because it is the one branch where we affirmatively know
            // the authorization is unusable to us. service_disabled qualifies: the token itself is fine,
            // but the operator has to enable the API and reconnect regardless, so leaving no trace is the
            // tidier outcome. A JsonException on a malformed success body does not — Drive answered 200,
            // the token is probably good, so it propagates untouched. When in doubt, do not revoke.
            await TryRevokeAsync(target.Key, grant.RefreshToken);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Throttling and network blips arrive here — 0f3389d3 made Drive's throttling 403 an
            // HttpRequestException on purpose, so that a transient second cannot be read as a dead grant.
            // Nothing is wrong with the authorization, so it is neither revoked nor reported as an auth
            // fault; without this arm it fell through to a 500. The TaskCanceledException case swallows an
            // aborted request into a 400, matching ValidateBackupTargetConnectionHandler — the response of
            // a request nobody is waiting for is discarded either way.
            logger.LogInformation(ex,
                "Could not reach Google to identify the account connecting backup target {TargetKey}",
                target.Key);

            throw new BusinessRuleException(
                "Could not reach Google just now — nothing was connected. Try again.");
        }

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

        // After the commit, never inline: SaveAsync's flush is joined to the pipeline transaction, so the
        // token manager — which reads the grant from its own scope on a separate connection — would see
        // NotConnected. Eager creation is what makes the share-with-a-spouse workflow possible at all; you
        // cannot share a folder that does not exist yet.
        //
        // CancellationToken.None, exactly as the confirm slice's revoke hook is: the provisioner swallows
        // everything BUT cancellation, and the request token is already cancelled in the case that produces
        // this path most often — the admin closing the tab. Honouring it would throw out of a connect that
        // has already committed, and skip the folder the next validate would then have to repair.
        unitOfWork.OnAfterCommit(_ => rootProvisioner.EnsureAsync(target, CancellationToken.None));

        return new PollBackupTargetConnectionResponse
        {
            Status = BackupConnectStatus.Connected,
            ConnectedAccountEmail = account.EmailAddress ?? existingEmail,
        };
    }

    /// <summary>
    /// Revokes a grant we are about to abandon, without ever letting that get in the way of the diagnosis.
    /// </summary>
    /// <remarks>
    /// Its own try/catch on top of <see cref="IGoogleOAuthClient.RevokeAsync"/>'s: that one is best-effort
    /// for transport failures only, so anything else escaping a <c>catch { revoke; throw; }</c> block would
    /// replace the classified <see cref="GoogleDriveAuthException"/> on the way out and destroy the
    /// diagnosis <c>0f3389d3</c> exists to provide. <see cref="CancellationToken.None"/> because the paths
    /// that lead here include the admin closing the tab, and a cancelled revoke is a revoke that did not
    /// happen.
    /// </remarks>
    private async Task TryRevokeAsync(string targetKey, string refreshToken)
    {
        try
        {
            await oauth.RevokeAsync(refreshToken, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not revoke the discarded Google authorization for backup target {TargetKey}; it may " +
                "linger in the account's third-party access page", targetKey);
        }
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
