using System.Net;
using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// The OAuth half of the Drive target: device authorization, refresh, and revocation.
/// </summary>
/// <remarks>
/// Hand-rolled against the four endpoints rather than delegated to <c>Google.Apis.Auth</c>, because that
/// library owns credential <i>storage</i> as well as protocol — it wants a file store or its own
/// datastore, and would decide when to refresh. Here the grant is an encrypted blob on a row we own and
/// refresh is driven by our own cache, so only the wire format is borrowed. The SDK is still used for
/// Drive itself, where the resumable-upload machinery is worth having.
/// </remarks>
public interface IGoogleOAuthClient
{
    Task<GoogleDeviceAuthorization> StartDeviceAuthorizationAsync(string clientId, CancellationToken ct);

    Task<GoogleDevicePollResult> PollDeviceTokenAsync(
        string clientId, string clientSecret, string deviceCode, CancellationToken ct);

    /// <summary>
    /// Exchanges a refresh token for a fresh access token.
    /// </summary>
    /// <exception cref="GoogleDriveAuthException">The authorization is dead (revoked, expired, wrong client).</exception>
    /// <exception cref="HttpRequestException">A transient failure. Must NOT be treated as a dead grant.</exception>
    Task<GoogleTokenGrant> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken ct);

    /// <summary>Best-effort revocation. Never throws — a token we cannot revoke is still one we forget.</summary>
    Task RevokeAsync(string token, CancellationToken ct);
}

public class GoogleOAuthClient(HttpClient httpClient, TimeProvider timeProvider, ILogger<GoogleOAuthClient> logger)
    : IGoogleOAuthClient
{
    private const string DeviceCodeEndpoint = "https://oauth2.googleapis.com/device/code";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    /// <summary>
    /// The only scope requested, ever. Non-sensitive (so the consent screen publishes with no verification
    /// review) and limited to files this app created — which is sufficient by construction, because the
    /// mirror never touches anything outside its own tree.
    /// </summary>
    public const string Scope = "https://www.googleapis.com/auth/drive.file";

    // Google's device-flow poll interval when it declines to state one.
    private const int DefaultPollIntervalSeconds = 5;

    public async Task<GoogleDeviceAuthorization> StartDeviceAuthorizationAsync(
        string clientId, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(
            DeviceCodeEndpoint,
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("scope", Scope),
            ]),
            ct);

        var body = await ReadJsonAsync(response, ct);

        if (!response.IsSuccessStatusCode)
        {
            var code = ReadString(body, "error") ?? response.StatusCode.ToString();

            // The two failures worth naming: a client id that does not exist, and a client of the wrong
            // TYPE (a Web application client rejects the device flow with invalid_scope, which reads as a
            // scope problem and is not one).
            throw new GoogleDriveAuthException(code, code switch
            {
                "invalid_client" => "Google rejected the OAuth client id — check Backup:Targets:{key}:ClientId.",
                "invalid_scope" =>
                    "Google rejected the drive.file scope for this client. The OAuth client must be of type " +
                    "\"TVs & Limited Input devices\" — a Web application client cannot use the device flow.",
                _ => $"Google refused to start the device authorization ({code})."
            });
        }

        var deviceCode = ReadString(body, "device_code")
            ?? throw new GoogleDriveAuthException("malformed_response",
                "Google's device authorization response carried no device code.");

        var expiresIn = ReadInt(body, "expires_in") ?? 1800;

        return new GoogleDeviceAuthorization(
            DeviceCode: deviceCode,
            UserCode: ReadString(body, "user_code") ?? string.Empty,
            // Google sends verification_url; the RFC calls it verification_uri. Accept either.
            VerificationUrl: ReadString(body, "verification_url")
                ?? ReadString(body, "verification_uri")
                ?? "https://www.google.com/device",
            IntervalSeconds: ReadInt(body, "interval") ?? DefaultPollIntervalSeconds,
            ExpiresAt: timeProvider.GetUtcNow().AddSeconds(expiresIn));
    }

    public async Task<GoogleDevicePollResult> PollDeviceTokenAsync(
        string clientId, string clientSecret, string deviceCode, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            ]),
            ct);

        var body = await ReadJsonAsync(response, ct);

        if (response.IsSuccessStatusCode)
        {
            var grant = ReadGrant(body, existingRefreshToken: null);

            // A device flow that returns no refresh token is unusable for a backup: the whole point is
            // running unattended weeks later. Treat it as a failure rather than storing an hour-long
            // credential that reads as connected and dies overnight.
            if (grant is null)
                throw new GoogleDriveAuthException("malformed_response",
                    "Google returned an authorization with no refresh token. Reconnect, and if it recurs " +
                    "revoke Homestead's access in your Google account and try again.");

            return new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, grant);
        }

        var error = ReadString(body, "error");

        // Non-terminal states are the normal path here — the admin is still typing the code — so they are
        // logged at nothing and simply reported upward.
        return error switch
        {
            "authorization_pending" => new GoogleDevicePollResult(GoogleDevicePollStatus.Pending),
            "slow_down" => new GoogleDevicePollResult(GoogleDevicePollStatus.SlowDown),
            "expired_token" => new GoogleDevicePollResult(GoogleDevicePollStatus.Expired),
            "access_denied" => new GoogleDevicePollResult(GoogleDevicePollStatus.Denied),
            _ => throw new GoogleDriveAuthException(
                error ?? response.StatusCode.ToString(),
                $"Google refused the device authorization ({error ?? response.StatusCode.ToString()}).")
        };
    }

    public async Task<GoogleTokenGrant> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(
                TokenEndpoint,
                new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                    new KeyValuePair<string, string>("refresh_token", refreshToken),
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                ]),
                ct);
        }
        catch (HttpRequestException)
        {
            // Deliberately NOT converted to GoogleDriveAuthException: the caller latches the target as
            // permanently disconnected on that type, and a network blip must never do that.
            throw;
        }

        using (response)
        {
            var body = await ReadJsonAsync(response, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = ReadString(body, "error");

                // A 5xx is Google having a bad day, not a dead grant — same reasoning as the catch above.
                if (response.StatusCode >= HttpStatusCode.InternalServerError)
                    throw new HttpRequestException(
                        $"Google's token endpoint returned {(int)response.StatusCode}.", null, response.StatusCode);

                logger.LogWarning(
                    "Google token refresh failed with OAuth error {ErrorCode} (HTTP {Status})",
                    error ?? "(none)", (int)response.StatusCode);

                throw new GoogleDriveAuthException(
                    error ?? response.StatusCode.ToString(),
                    error switch
                    {
                        "invalid_grant" =>
                            "Google rejected the stored authorization (invalid_grant) — it was revoked or expired.",
                        "invalid_client" =>
                            "Google rejected the OAuth client credentials — check ClientId and ClientSecret.",
                        _ => $"Google rejected the stored authorization ({error ?? "unknown error"})."
                    });
            }

            // Google does not rotate refresh tokens on an ordinary refresh, so the one we sent stays
            // current unless a new one comes back.
            return ReadGrant(body, existingRefreshToken: refreshToken)
                ?? throw new GoogleDriveAuthException("malformed_response",
                    "Google's token response carried no access token.");
        }
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        // Best-effort by design. Google does NOT invalidate old refresh tokens when issuing new ones, so
        // skipping revocation on every disconnect/reconnect would strand a live credential in the user's
        // Google security page forever — but a revocation we cannot deliver must not block the disconnect
        // itself, or a target whose config vanished could never be cleaned up.
        try
        {
            using var response = await httpClient.PostAsync(
                RevokeEndpoint,
                new FormUrlEncodedContent([new KeyValuePair<string, string>("token", token)]),
                ct);

            if (!response.IsSuccessStatusCode)
                logger.LogInformation(
                    "Google token revocation returned HTTP {Status}; the local grant is cleared regardless",
                    (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex,
                "Google token revocation could not be delivered; the local grant is cleared regardless");
        }
    }

    // --- Wire helpers -------------------------------------------------------------------------------
    //
    // Parsed as a JsonElement rather than into DTOs: every one of these bodies contains a credential or a
    // device code, and a typed model invites someone to ToString() it into a log line. Reading named
    // fields out keeps the secret in exactly one variable at a time. Nothing here ever logs a body.

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // An HTML error page from a proxy, say. Treated as "no fields", which the callers already
            // handle by falling back to the status code.
            return default;
        }
    }

    private GoogleTokenGrant? ReadGrant(JsonElement body, string? existingRefreshToken)
    {
        var accessToken = ReadString(body, "access_token");
        if (accessToken is null)
            return null;

        var refreshToken = ReadString(body, "refresh_token") ?? existingRefreshToken;
        if (refreshToken is null)
            return null;

        // 60 s of slack so a token that is technically alive when we check is still alive when the request
        // it authorizes reaches Google.
        var expiresIn = ReadInt(body, "expires_in") ?? 3600;
        var expiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(0, expiresIn - 60));

        return new GoogleTokenGrant(refreshToken, accessToken, expiresAt);
    }

    private static string? ReadString(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement body, string name)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            // Google has been known to send expires_in as a string in some flows.
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}
