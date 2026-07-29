using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <param name="StorageLimitBytes">Null means unlimited — not "zero".</param>
public record GoogleDriveAccountInfo(
    string? EmailAddress,
    long? StorageUsedBytes,
    long? StorageLimitBytes);

/// <summary>
/// Reads <c>about.get</c> for a raw access token.
/// </summary>
/// <remarks>
/// Separate from the target's own <c>DriveService</c> because both callers need it <i>before</i> a grant
/// is stored: the connect poll asks "whose account did we just get?" with a token that is not yet on any
/// row, and validate probes with a token it may be about to discard. Building a whole service around a
/// throwaway credential to make one GET is not worth it.
/// </remarks>
public interface IGoogleDriveAccountClient
{
    /// <summary>Reads the connected account's address and quota.</summary>
    /// <exception cref="GoogleDriveAuthException">
    /// The authorization (or the Cloud project behind it) needs an operator: a rejected token, a Drive API
    /// that was never enabled, a consent without the Drive scope. Callers <b>latch</b> the target on this.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// A transient failure, <b>including Drive's throttling 403s</b>. Must NOT be treated as a dead grant.
    /// </exception>
    Task<GoogleDriveAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct);
}

public class GoogleDriveAccountClient(HttpClient httpClient, ILogger<GoogleDriveAccountClient> logger)
    : IGoogleDriveAccountClient
{
    private const string AboutEndpoint =
        "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress),storageQuota(limit,usage)";

    /// <summary>
    /// Above this we do not bother parsing. Orders of magnitude beyond any real Drive error, so anything
    /// larger is a proxy's HTML page, which has no fields worth looking for.
    /// </summary>
    private const int MaxErrorBodyBytes = 16 * 1024;

    public async Task<GoogleDriveAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AboutEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw await ClassifyRefusalAsync(response, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        // Through ReadString rather than a raw GetString(): that one throws on a value that is not a string,
        // which would leave a proxy's creative idea of JSON escaping as an unclassifiable 500.
        var email = root.TryGetProperty("user", out var user) ? ReadString(user, "emailAddress") : null;

        long? usage = null;
        long? limit = null;
        if (root.TryGetProperty("storageQuota", out var quota))
        {
            usage = ReadLong(quota, "usage");
            // Absent on an unlimited account, which is a real state and not zero — so it stays null all
            // the way to the card rather than rendering as "0 bytes free".
            limit = ReadLong(quota, "limit");
        }

        return new GoogleDriveAccountInfo(email, usage, limit);
    }

    // Drive sends 64-bit quota values as JSON strings.
    private static long? ReadLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    // --- Refusals -------------------------------------------------------------------------------------

    /// <summary>
    /// Turns a 401/403 into a message that names the fix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worth the parsing, because this call is where setup mistakes surface and the status code alone
    /// points at the wrong thing. The two overwhelmingly likely causes are <b>both 403s that arrive after
    /// a flawless consent flow</b> — the Drive API never enabled on the Cloud project, and the Drive
    /// permission left unticked on Google's granular-consent screen. This is the first request the whole
    /// feature makes to <c>drive/v3</c> (the device flow only ever talks to the OAuth endpoints, which
    /// care about neither), so it is the first moment either can possibly be detected, and reporting them
    /// as "Google rejected the access token" accuses the one thing that was fine.
    /// </para>
    /// <para>
    /// Unlike the token-endpoint bodies the OAuth client deliberately never reads, an <c>about.get</c>
    /// error body carries no credential — so it is safe to parse, and safe to log the codes out of.
    /// </para>
    /// <para>
    /// Returns <see cref="Exception"/>, not <see cref="GoogleDriveAuthException"/>, because not every 403
    /// is an authorization fault: Drive throttles with one too, and only the reason string tells them
    /// apart.
    /// </para>
    /// </remarks>
    private async Task<Exception> ClassifyRefusalAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var error = await ReadErrorAsync(response, ct);

        // Codes only, never the body — the same posture as the OAuth client. Enough to tell the two 403s
        // apart from a server log alone, which is the point.
        logger.LogWarning(
            "Google Drive refused about.get: HTTP {Status}, error status {ErrorStatus}, reasons {Reasons}",
            (int)response.StatusCode,
            error.Status ?? "(none)",
            error.Reasons.Count > 0 ? string.Join(",", error.Reasons) : "(none)");

        // Drive reports throttling as a 403 as well, told apart from a real authorization fault only by
        // the reason string — so this has to be answered FIRST, and must not become a
        // GoogleDriveAuthException. The validate handler latches the target on that type and the gate then
        // refuses every scheduled run until a human re-validates by hand: a permanent consequence for a
        // condition that clears itself in seconds. The mirror path already reads these same reasons as
        // retryable; reading them differently here is what would make one throttled probe stop backups.
        if (error.MatchReason(GoogleDriveErrorReasons.Throttling) is { } throttled)
        {
            return new HttpRequestException(
                $"Google throttled the Drive request ({throttled}).", null, response.StatusCode);
        }

        // Google reports this one twice, under two different spellings, in two different places.
        if (error.HasReason(GoogleDriveErrorReasons.ServiceDisabled))
        {
            return new GoogleDriveAuthException("service_disabled",
                "The Google Drive API is not enabled on the Google Cloud project that owns this OAuth "
                + "client. Enable it (APIs & Services → Library → Google Drive API), then connect again."
                + (error.HelpUrl is { Length: > 0 } help ? $" Google's direct link: {help}" : string.Empty));
        }

        // Normally caught earlier, at the token exchange — this is the path for when Google declined to
        // state the granted scope there.
        if (error.HasReason(GoogleDriveErrorReasons.InsufficientScope))
            return new GoogleDriveAuthException("insufficient_scope", GoogleDriveAuthMessages.InsufficientScope);

        if (response.StatusCode is HttpStatusCode.Forbidden)
        {
            var reason = error.Reasons.FirstOrDefault() ?? error.Status;

            // The code stays a code even when Google named nothing — it is logged as one and compared
            // against one (GoogleDriveGrantStore.BuildReason); a sentence with spaces in it does not
            // belong there. The human phrasing lives in the message.
            return new GoogleDriveAuthException(reason ?? "forbidden",
                $"Google refused the Drive request ({reason ?? "no reason given"})."
                + (error.Message is { Length: > 0 } detail ? $" Google's explanation: {detail}" : string.Empty));
        }

        return new GoogleDriveAuthException("unauthorized",
            "Google rejected the access token when reading the connected account.");
    }

    /// <summary>What a Google API error body says, reduced to the fields worth acting on.</summary>
    /// <param name="Reasons">
    /// Both spellings, pooled. The legacy <c>error.errors[].reason</c> (<c>accessNotConfigured</c>) and
    /// the newer <c>error.details[].reason</c> (<c>SERVICE_DISABLED</c>) name the same fault, and which
    /// one is present varies by API and by year — so matching looks in both.
    /// </param>
    private record GoogleApiError(
        string? Status,
        IReadOnlyList<string> Reasons,
        string? Message,
        string? HelpUrl)
    {
        /// <summary>The first stated reason among <paramref name="candidates"/>, or null for no match.</summary>
        public string? MatchReason(params string[] candidates) =>
            Reasons.FirstOrDefault(r => candidates.Contains(r, StringComparer.OrdinalIgnoreCase));

        public bool HasReason(params string[] candidates) => MatchReason(candidates) is not null;

        public static GoogleApiError None { get; } = new(null, [], null, null);
    }

    private static async Task<GoogleApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            // Read through a fixed buffer rather than ReadAsByteArrayAsync-then-measure: a cap applied to
            // an already-buffered body prevents the parse but not the allocation it exists to prevent. One
            // byte of headroom so a body that exactly fills the buffer is still recognisable as too big.
            await using var stream = await response.Content.ReadAsStreamAsync(ct);

            var buffer = new byte[MaxErrorBodyBytes + 1];
            var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);

            if (length is 0 || length > MaxErrorBodyBytes)
                return GoogleApiError.None;

            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));

            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind is not JsonValueKind.Object)
            {
                return GoogleApiError.None;
            }

            var reasons = new List<string>();
            CollectReasons(error, "errors", reasons);
            CollectReasons(error, "details", reasons);

            return new GoogleApiError(
                Status: ReadString(error, "status"),
                Reasons: reasons,
                Message: ReadString(error, "message"),
                HelpUrl: ReadHelpUrl(error));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An HTML error page from a proxy, a body that died mid-read, whatever. Treated as "no fields",
            // which the caller handles by falling back to the status code. Deliberately everything except
            // cancellation: this is a best-effort diagnostic read, and losing the 401/403 verdict itself
            // because the body would not parse — or would not finish arriving — is strictly worse than
            // reporting it without detail.
            return GoogleApiError.None;
        }
    }

    private static void CollectReasons(JsonElement error, string arrayName, List<string> into)
    {
        foreach (var entry in EnumerateObjects(error, arrayName))
        {
            if (ReadString(entry, "reason") is { Length: > 0 } reason)
                into.Add(reason);
        }
    }

    /// <summary>The object entries of a named array property; nothing when it is absent or not an array.</summary>
    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement parent, string arrayName)
    {
        if (!parent.TryGetProperty(arrayName, out var array) || array.ValueKind is not JsonValueKind.Array)
            yield break;

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind is JsonValueKind.Object)
                yield return entry;
        }
    }

    /// <summary>
    /// The one-click enable link, under either of the two shapes Google states it in.
    /// </summary>
    /// <remarks>
    /// Carried into the message because for the disabled-API case it <i>is</i> the fix — it lands on the
    /// right API in the right project without the operator having to work out which project that is. Both
    /// shapes are read for the same reason both reason spellings are: the response that names the fault
    /// <i>only</i> in <c>details[]</c> is exactly the one that would otherwise lose the link.
    /// </remarks>
    private static string? ReadHelpUrl(JsonElement error)
    {
        // Legacy shape: extendedHelp on the errors[] entry.
        foreach (var entry in EnumerateObjects(error, "errors"))
        {
            if (ReadString(entry, "extendedHelp") is { Length: > 0 } url)
                return url;
        }

        // Newer shape: a google.rpc.Help detail carrying links[].url.
        foreach (var entry in EnumerateObjects(error, "details"))
        {
            foreach (var link in EnumerateObjects(entry, "links"))
            {
                if (ReadString(link, "url") is { Length: > 0 } url)
                    return url;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
