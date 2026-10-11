using Homestead.WebApi.Common;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConnectBackupTarget;

/// <summary>
/// Starts a device authorization for a Google Drive target.
/// </summary>
/// <remarks>
/// The device flow rather than the conventional redirect, and not as a convenience: Google refuses
/// non-HTTPS and raw-IP redirect URIs (which excludes plain-HTTP and LAN-IP deployments — a large slice
/// of this app's audience), and a redirect callback is an unauthenticated cross-site request that would
/// need signed-state, correlation-cookie, and PKCE machinery to stop an attacker splicing <i>their</i>
/// Google account into the flow and receiving the library on the next run. This endpoint and its poll
/// sibling are ordinary admin-authenticated same-site calls; that attack surface does not exist here.
/// </remarks>
public class ConnectBackupTargetHandler(
    IBackupTargetResolver resolver,
    IBackupTargetOptionsAccessor options,
    IGoogleOAuthClient oauth,
    IGoogleDriveConnectTokenProtector protector)
{
    public async Task<ConnectBackupTargetResponse> HandleAsync(
        string key, CancellationToken ct)
    {
        var target = resolver.Find(key)
            ?? throw new EntityNotFoundException($"Backup target '{key}' is not configured.");

        if (target.Kind is not BackupTargetKind.GoogleDrive)
            throw new BusinessRuleException($"Backup target '{target.Key}' is not a Google Drive target.");

        // Config-shape only — there is nothing to compose against yet, since connecting is what creates
        // the state this target does not have.
        if (target.ConfigurationError is { } configurationError)
            throw new BusinessRuleException(
                $"Backup target '{target.Key}' is not configured correctly: {configurationError}");

        var clientId = options.Get(target.Key).ClientId!;

        var authorization = await oauth.StartDeviceAuthorizationAsync(clientId, ct);

        return new ConnectBackupTargetResponse(
            authorization.UserCode,
            authorization.VerificationUrl,
            authorization.ExpiresAt,
            authorization.IntervalSeconds,
            protector.ProtectPoll(new GoogleConnectPollToken(
                target.Key, authorization.DeviceCode, authorization.ExpiresAt)));
    }
}
