using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <summary>The in-flight device authorization, as round-tripped through the browser.</summary>
public record GoogleConnectPollToken(string TargetKey, string DeviceCode, DateTimeOffset ExpiresAt);

/// <summary>A granted authorization held back pending an explicit "yes, switch accounts".</summary>
public record GoogleConnectPendingGrant(
    string TargetKey,
    GoogleTokenGrant Grant,
    string? AccountEmail,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Seals the connect flow's intermediate state so the server can stay stateless across polls.
/// </summary>
/// <remarks>
/// <para>
/// The device code is a bearer-grade secret for the length of the flow — whoever holds it collects the
/// resulting token — and so is a granted-but-unstored grant. Neither may sit in the browser in the
/// clear, and neither is worth a server-side session table for the ninety seconds it lives. Data
/// Protection gives both: the payload is opaque and tamper-evident, and the embedded expiry means a
/// stolen blob is useless within minutes.
/// </para>
/// <para>
/// A distinct purpose from the stored-grant protector — and one purpose <i>per payload</i>, not one for
/// both: sharing a purpose would let a poll token be presented where a pending grant is expected, which
/// deserializes into a pending grant with a null <c>Grant</c> and drives the confirm slice into wiping
/// every recorded copy before storing a credential that is not one.
/// </para>
/// </remarks>
public interface IGoogleDriveConnectTokenProtector
{
    string ProtectPoll(GoogleConnectPollToken token);
    GoogleConnectPollToken? UnprotectPoll(string value);

    string ProtectPending(GoogleConnectPendingGrant pending);
    GoogleConnectPendingGrant? UnprotectPending(string value);
}

public class GoogleDriveConnectTokenProtector(
    IDataProtectionProvider dataProtection, TimeProvider timeProvider) : IGoogleDriveConnectTokenProtector
{
    public const string ProtectorPurpose = "Homestead.Backup.GoogleDrive.Connect";

    public const string PollPurpose = ProtectorPurpose + ".Poll";
    public const string PendingPurpose = ProtectorPurpose + ".Pending";

    private readonly IDataProtector _pollProtector = dataProtection.CreateProtector(PollPurpose);
    private readonly IDataProtector _pendingProtector = dataProtection.CreateProtector(PendingPurpose);

    public string ProtectPoll(GoogleConnectPollToken token) =>
        _pollProtector.Protect(JsonSerializer.Serialize(token));

    public GoogleConnectPollToken? UnprotectPoll(string value) =>
        Unprotect<GoogleConnectPollToken>(_pollProtector, value) is { } token
        && token.ExpiresAt > timeProvider.GetUtcNow()
            ? token
            : null;

    public string ProtectPending(GoogleConnectPendingGrant pending) =>
        _pendingProtector.Protect(JsonSerializer.Serialize(pending));

    public GoogleConnectPendingGrant? UnprotectPending(string value) =>
        // Grant is checked explicitly: a positional record's non-nullable member is still null when the
        // payload simply had no such property, and storing that would read as "connected" while holding
        // nothing.
        Unprotect<GoogleConnectPendingGrant>(_pendingProtector, value) is { Grant: not null } pending
        && pending.ExpiresAt > timeProvider.GetUtcNow()
            ? pending
            : null;

    private static T? Unprotect<T>(IDataProtector protector, string value)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(protector.Unprotect(value));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            // Tampered, expired key ring, or simply garbage. All read as "this token is not valid",
            // which is the only distinction the caller needs.
            return default;
        }
    }
}
