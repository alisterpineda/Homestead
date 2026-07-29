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

    /// <summary>
    /// A live credential this failure means we will never use, for the caller to hand back to Google.
    /// </summary>
    /// <remarks>
    /// Carried out rather than revoked where it is discovered, because whether revoking is <i>safe</i> is
    /// not knowable there: Google's <c>/revoke</c> is GRANT-scoped, so it drops every token this client
    /// holds for that account. Only the caller knows whether a live grant is already on file for the
    /// target — and revoking then takes out a working mirror rather than one stale token.
    /// </remarks>
    public string? OrphanedToken { get; init; }
}

/// <summary>Wording shared by two detectors of one fault.</summary>
/// <remarks>
/// A consent screen approved <i>without</i> ticking the Drive permission is caught in two places: at the
/// token exchange when Google states the granted scope, and at the first Drive call when it does not.
/// Same fault, same fix — so one sentence, rather than two that drift apart.
/// </remarks>
internal static class GoogleDriveAuthMessages
{
    public const string InsufficientScope =
        "Google issued an authorization that does not include Drive access. On the consent screen, tick "
        + "the Google Drive permission — it is a checkbox, and is not ticked for you — then connect again.";
}

/// <summary>
/// The Google <c>reason</c> strings this feature acts on, in one place.
/// </summary>
/// <remarks>
/// Drive answers throttling, storage exhaustion, and genuine authorization faults <b>all with HTTP
/// 403</b>, so the reason string is the only thing separating "retry in a moment" from "an operator has
/// to go and fix something". Two places read them — the mirror's SDK path and the raw <c>about.get</c>
/// probe — and a list only one of them knows about is precisely how a transient blip ends up latching a
/// target as permanently disconnected.
/// </remarks>
internal static class GoogleDriveErrorReasons
{
    /// <summary>
    /// Transient. Note that <c>quotaExceeded</c>, confusingly, means <i>rate</i> and not storage.
    /// </summary>
    public static readonly string[] Throttling =
    [
        "userRateLimitExceeded", "rateLimitExceeded", "quotaExceeded", "dailyLimitExceeded",
        // The newer details[].reason spellings of the same faults.
        "RATE_LIMIT_EXCEEDED", "RESOURCE_EXHAUSTED",
    ];

    /// <summary>Storage exhaustion — a 403 like the others, and the one that is never retryable.</summary>
    public static readonly string[] OutOfSpace = ["storageQuotaExceeded"];

    /// <summary>The Drive API is not enabled on the Cloud project. Stated under two spellings.</summary>
    public static readonly string[] ServiceDisabled = ["accessNotConfigured", "SERVICE_DISABLED"];

    /// <summary>The authorization carries no Drive scope. Also stated under two spellings.</summary>
    public static readonly string[] InsufficientScope =
        ["insufficientPermissions", "ACCESS_TOKEN_SCOPE_INSUFFICIENT"];
}
