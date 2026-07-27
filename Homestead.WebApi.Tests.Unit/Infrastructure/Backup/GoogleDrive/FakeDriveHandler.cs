using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

/// <summary>One recorded call to the Drive API.</summary>
public record DriveCall(HttpMethod Method, string Url, string Body)
{
    /// <summary>The `q=` search expression, url-decoded. Empty for anything that is not a list.</summary>
    public string Query =>
        Regex.Match(Url, @"[?&]q=([^&]*)") is { Success: true } match
            ? Uri.UnescapeDataString(match.Groups[1].Value).Replace('+', ' ')
            : string.Empty;
}

/// <summary>
/// A scripted Drive API. Matches on (method, URL fragment) so a test scripts only the calls it cares
/// about and anything unmatched falls through to a benign default.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately dumb: a transcript recorder plus a response queue, not a Drive emulator. What these
/// tests assert is the <i>request</i> the target makes — that a delete is a trashed:true patch and never
/// a DELETE, that an id-addressed update always asserts trashed:false, that a name query is escaped —
/// none of which needs the destination to behave like Drive.
/// </para>
/// <para>
/// The one place it does have to play along is the resumable-upload handshake, which is two requests:
/// an initiate that must answer with a <c>Location</c> header, and a chunk PUT to that location which
/// carries the response body. A test scripts the <i>initiate</i> and the body surfaces on the PUT.
/// </para>
/// </remarks>
public class FakeDriveHandler : HttpMessageHandler
{
    private const string SessionPrefix = "https://upload.test/session/";

    private readonly List<(Func<HttpRequestMessage, bool> Match, HttpStatusCode Status, string Body)> _rules = [];
    private readonly Dictionary<string, string> _pendingUploads = new(StringComparer.Ordinal);

    private int _sessionCounter;

    public List<DriveCall> Calls { get; } = [];

    /// <summary>Served when nothing matched: an empty list and a plausible file object.</summary>
    public string DefaultBody { get; set; } = """{"files":[],"id":"default-id"}""";

    public FakeDriveHandler Respond(
        HttpMethod method, string urlContains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _rules.Add((
            request => request.Method == method
                       && (request.RequestUri?.ToString().Contains(urlContains, StringComparison.Ordinal) ?? false),
            status,
            body));

        return this;
    }

    /// <summary>Matches a list request by a fragment of its `q=` expression.</summary>
    public FakeDriveHandler RespondToQuery(string queryContains, string body)
    {
        _rules.Add((
            request => request.Method == HttpMethod.Get
                       && Decode(request).Contains(queryContains, StringComparison.Ordinal),
            HttpStatusCode.OK,
            body));

        return this;
    }

    public FakeDriveHandler Fail(HttpMethod method, string urlContains, HttpStatusCode status, string reason)
    {
        var body =
            $$$"""
            {"error":{"code":{{{(int)status}}},"message":"{{{reason}}}",
             "errors":[{"reason":"{{{reason}}}","message":"{{{reason}}}","domain":"global"}]}}
            """;

        return Respond(method, urlContains, body, status);
    }

    public IEnumerable<DriveCall> Where(HttpMethod method) => Calls.Where(c => c.Method == method);

    private static string Decode(HttpRequestMessage request) =>
        Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty).Replace('+', ' ');

    private static bool IsUploadInitiate(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.Contains("/upload/", StringComparison.Ordinal) == true;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        var body = request.Content is null
            ? string.Empty
            : await ReadSafelyAsync(request, cancellationToken);

        Calls.Add(new DriveCall(request.Method, url, body));

        // The chunk PUT: hand back whatever the initiate reserved for it.
        if (url.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            _pendingUploads.Remove(url, out var reserved);
            return Json(HttpStatusCode.OK, reserved ?? DefaultBody);
        }

        var matched = _rules.FirstOrDefault(r => r.Match(request));
        if (matched.Body is not null)
            _rules.Remove(matched);

        var status = matched.Body is null ? HttpStatusCode.OK : matched.Status;
        var responseBody = matched.Body ?? DefaultBody;

        // An initiate that is going to fail says so immediately — there is no session to open.
        if (!IsUploadInitiate(request) || !IsSuccess(status))
            return Json(status, responseBody);

        var sessionUri = $"{SessionPrefix}{++_sessionCounter}";
        _pendingUploads[sessionUri] = responseBody;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
        };
        response.Headers.Location = new Uri(sessionUri);

        return response;
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    // The SDK gzips request bodies, and several tests assert on the metadata JSON it sends — so the
    // transcript has to hold the decompressed text. Stream content is not always re-readable either; a
    // failure to capture must never fail the call.
    private static async Task<string> ReadSafelyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var bytes = await request.Content!.ReadAsByteArrayAsync(ct);

            if (!request.Content.Headers.ContentEncoding.Contains("gzip"))
                return Encoding.UTF8.GetString(bytes);

            using var compressed = new MemoryStream(bytes);
            await using var gzip = new System.IO.Compression.GZipStream(
                compressed, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            return await reader.ReadToEndAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
