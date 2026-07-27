namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// What Google hands back when a device authorization starts: the code the admin types at
/// <see cref="VerificationUrl"/>, and the code <i>we</i> poll with.
/// </summary>
/// <remarks>
/// <see cref="DeviceCode"/> is a bearer-grade secret for the length of the flow — anyone holding it can
/// collect the resulting token — so it never reaches a log, and never reaches the browser in the clear
/// (the connect endpoint round-trips it inside a Data-Protection-protected poll token instead).
/// </remarks>
public record GoogleDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    string VerificationUrl,
    int IntervalSeconds,
    DateTimeOffset ExpiresAt);

/// <summary>
/// A live authorization. This is the object that gets serialized and encrypted onto
/// <c>backup.target_state.EncryptedGrant</c> — one blob rather than a column per field, so adding a
/// field later is not a migration and no half of a credential can be read without the other.
/// </summary>
public record GoogleTokenGrant(
    string RefreshToken,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);

public enum GoogleDevicePollStatus
{
    /// <summary>The admin has not finished consenting. Keep polling at the stated interval.</summary>
    Pending,

    /// <summary>We polled too fast. Keep polling, slower.</summary>
    SlowDown,

    /// <summary>Consent granted; <see cref="GoogleDevicePollResult.Grant"/> is populated.</summary>
    Granted,

    /// <summary>The device code aged out before anyone consented. Terminal — start over.</summary>
    Expired,

    /// <summary>The admin (or a Workspace policy) refused. Terminal.</summary>
    Denied
}

public record GoogleDevicePollResult(GoogleDevicePollStatus Status, GoogleTokenGrant? Grant = null);

/// <summary>
/// A Google-side refusal of our credentials, as opposed to a network failure.
/// </summary>
/// <remarks>
/// <b>The distinction is the whole point of this type.</b> An <c>invalid_grant</c> means the
/// authorization is dead and the target must latch as unavailable until someone reconnects; a DNS
/// hiccup or a 502 means try again in five minutes. Conflating them would let one bad minute of
/// connectivity permanently mark a working backup as disconnected — so transient failures surface as
/// plain <see cref="HttpRequestException"/> and only this type latches.
/// </remarks>
public class GoogleDriveAuthException(string errorCode, string message) : Exception(message)
{
    /// <summary>The OAuth error code (e.g. <c>invalid_grant</c>). Safe to log — unlike the body it came in.</summary>
    public string ErrorCode { get; } = errorCode;
}
