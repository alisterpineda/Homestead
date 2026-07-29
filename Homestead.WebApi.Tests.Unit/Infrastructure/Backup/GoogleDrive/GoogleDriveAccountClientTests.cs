using System.Net;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// Covers <c>about.get</c>, and above all the classification of its refusals.
/// </summary>
/// <remarks>
/// This call is where Google Cloud setup mistakes surface: it is the first request the backup feature
/// makes to <c>drive/v3</c>, since the device flow only ever touches the OAuth endpoints. Two entirely
/// different setup faults arrive here as an identical HTTP 403 after a consent flow that looked perfect,
/// so the reason strings are the only thing separating "enable an API" from "re-consent" — hence a test
/// per shape, with bodies copied from what Google actually sends.
/// </remarks>
public class GoogleDriveAccountClientTests
{
    private static (GoogleDriveAccountClient Client, FakeGoogleHttpHandler Handler) CreateSut()
    {
        var handler = new FakeGoogleHttpHandler();

        return (
            new GoogleDriveAccountClient(
                new HttpClient(handler), NullLogger<GoogleDriveAccountClient>.Instance),
            handler);
    }

    // --- The happy path -------------------------------------------------------------------------------

    [Fact]
    public async Task GetAccountInfo_ReadsTheEmailAndQuota()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"user":{"emailAddress":"someone@example.test"},
             "storageQuota":{"usage":"1024","limit":"16106127360"}}
            """);

        var info = await client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken);

        Assert.Equal("someone@example.test", info.EmailAddress);
        Assert.Equal(1024, info.StorageUsedBytes);
        Assert.Equal(16106127360, info.StorageLimitBytes);

        Assert.Contains("drive/v3/about", handler.RequestUrls[0]);
    }

    // An unlimited account states its usage and omits the limit — the quota object is there, one field
    // short. Rendering that as "0 bytes free" would read as a full Drive, the opposite of the truth.
    [Fact]
    public async Task GetAccountInfo_AbsentStorageLimitStaysNullRatherThanZero()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"user":{"emailAddress":"someone@example.test"},"storageQuota":{"usage":"2048"}}
            """);

        var info = await client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken);

        Assert.Equal(2048, info.StorageUsedBytes);
        Assert.Null(info.StorageLimitBytes);
    }

    // A body with no storageQuota at all — the whole object missing rather than one field — must not
    // become zeroes either.
    [Fact]
    public async Task GetAccountInfo_NoQuotaObjectAtAll_LeavesEveryFieldNull()
    {
        var (client, _) = CreateSut();

        var info = await client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken);

        Assert.Null(info.EmailAddress);
        Assert.Null(info.StorageUsedBytes);
        Assert.Null(info.StorageLimitBytes);
    }

    // A proxy's creative idea of JSON must not escape as an unclassifiable 500 from a raw GetString().
    [Fact]
    public async Task GetAccountInfo_ANonStringEmailAddress_ReadsAsAbsentRatherThanThrowing()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""{"user":{"emailAddress":42},"storageQuota":{"usage":"5"}}""");

        var info = await client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken);

        Assert.Null(info.EmailAddress);
        Assert.Equal(5, info.StorageUsedBytes);
    }

    // --- The 403 that means "enable an API" -----------------------------------------------------------

    // The setup mistake this whole classification exists for. Every OAuth step succeeds — codes, consent,
    // token exchange — because none of them touches the Drive API; the first drive/v3 request then fails
    // with a 403 that says nothing about the access token, which is what the old message blamed.
    [Fact]
    public async Task GetAccountInfo_DriveApiNotEnabled_NamesTheApiAndCarriesGooglesEnableLink()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, """
            {"error":{"code":403,
              "message":"Google Drive API has not been used in project 12345 before or it is disabled.",
              "errors":[{"message":"Google Drive API has not been used in project 12345 before or it is disabled.",
                         "domain":"usageLimits","reason":"accessNotConfigured",
                         "extendedHelp":"https://console.developers.google.com/apis/api/drive.googleapis.com/overview?project=12345"}],
              "status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"SERVICE_DISABLED",
                          "domain":"googleapis.com","metadata":{"service":"drive.googleapis.com"}}]}}
            """);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("service_disabled", ex.ErrorCode);
        Assert.Contains("Google Drive API is not enabled", ex.Message);

        // The one-click fix: it lands on the right API in the right project, so the operator does not have
        // to work out which project the client belongs to.
        Assert.Contains("console.developers.google.com/apis/api/drive.googleapis.com", ex.Message);

        // The old message accused the token, which was the one thing that was fine.
        Assert.DoesNotContain("rejected the access token", ex.Message);
    }

    // Google states this fault twice under two spellings — the legacy errors[].reason and the newer
    // details[].reason — and which is present varies. Either alone must classify.
    [Theory]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"accessNotConfigured"}]}}""")]
    [InlineData("""{"error":{"code":403,"details":[{"reason":"SERVICE_DISABLED"}]}}""")]
    public async Task GetAccountInfo_DriveApiNotEnabled_ClassifiesFromEitherReasonSpelling(string body)
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, body);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("service_disabled", ex.ErrorCode);
    }

    // The link is stated twice as well, and the response that names the fault ONLY under the newer
    // spelling is exactly the one that states its help only under the newer spelling too — so reading just
    // the legacy extendedHelp would drop the one-click fix from the message that needs it most.
    [Fact]
    public async Task GetAccountInfo_DriveApiNotEnabled_FindsTheEnableLinkInTheNewerHelpShape()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, """
            {"error":{"code":403,"message":"Google Drive API has not been used in project 12345.",
              "status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"SERVICE_DISABLED"},
                         {"@type":"type.googleapis.com/google.rpc.Help",
                          "links":[{"description":"Google developers console API activation",
                                    "url":"https://console.developers.google.com/apis/api/drive.googleapis.com/overview?project=12345"}]}]}}
            """);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("service_disabled", ex.ErrorCode);
        Assert.Contains("console.developers.google.com/apis/api/drive.googleapis.com", ex.Message);
    }

    // --- The 403 that means "re-consent" --------------------------------------------------------------

    // Google renders Drive access as a tickbox. Approving without ticking it yields a valid token with no
    // Drive scope — indistinguishable from the disabled-API case by status code alone.
    [Fact]
    public async Task GetAccountInfo_InsufficientScope_PointsAtTheConsentTickbox()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, """
            {"error":{"code":403,"message":"Request had insufficient authentication scopes.",
              "errors":[{"message":"Insufficient Permission","domain":"global",
                         "reason":"insufficientPermissions"}],
              "status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo",
                          "reason":"ACCESS_TOKEN_SCOPE_INSUFFICIENT"}]}}
            """);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("insufficient_scope", ex.ErrorCode);
        Assert.Contains("checkbox", ex.Message);
    }

    // --- Everything else ------------------------------------------------------------------------------

    // An unrecognised 403 still has to hand over whatever Google said, rather than flattening to a
    // message about the access token.
    [Fact]
    public async Task GetAccountInfo_UnrecognisedForbidden_ForwardsGooglesReasonAndExplanation()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, """
            {"error":{"code":403,"message":"The user has not granted the app 12345 write access.",
              "errors":[{"reason":"appNotAuthorizedToFile"}],"status":"PERMISSION_DENIED"}}
            """);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("appNotAuthorizedToFile", ex.ErrorCode);
        Assert.Contains("appNotAuthorizedToFile", ex.Message);
        Assert.Contains("has not granted the app", ex.Message);
    }

    // Drive reports THROTTLING as a 403 as well, told apart from a dead authorization only by the reason
    // — and `quotaExceeded`, confusingly, means rate rather than storage. It must stay an
    // HttpRequestException: the validate handler latches the target on GoogleDriveAuthException and the
    // gate then refuses every scheduled run until a human re-validates, which is a permanent consequence
    // for a condition that clears itself in seconds. The mirror path already retries these same reasons.
    [Theory]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"userRateLimitExceeded"}]}}""")]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}""")]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"quotaExceeded"}]}}""")]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"dailyLimitExceeded"}]}}""")]
    [InlineData("""{"error":{"code":403,"details":[{"reason":"RESOURCE_EXHAUSTED"}]}}""")]
    public async Task GetAccountInfo_Throttled_StaysAnHttpRequestExceptionSoNothingLatches(string body)
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, body);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.IsNotType<GoogleDriveAuthException>(ex);
    }

    // A 401 is the one case the original message described correctly: a token Google will not accept.
    [Fact]
    public async Task GetAccountInfo_Unauthorized_KeepsTheAccessTokenMessage()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Unauthorized, """
            {"error":{"code":401,"message":"Request had invalid authentication credentials.",
              "errors":[{"reason":"authError"}],"status":"UNAUTHENTICATED"}}
            """);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("unauthorized", ex.ErrorCode);
        Assert.Contains("rejected the access token", ex.Message);
    }

    // A proxy or captive portal answering with HTML must not turn a 403 into a JSON parse crash.
    [Fact]
    public async Task GetAccountInfo_NonJsonErrorBody_FallsBackToTheStatusCode()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Forbidden, "<html><body>Forbidden</body></html>");

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        // A code stays a token — it is logged as one and compared against one. The human phrasing for
        // "Google named nothing" belongs in the message.
        Assert.Equal("forbidden", ex.ErrorCode);
        Assert.Contains("no reason given", ex.Message);
    }

    // A captive portal can answer with a page far bigger than any Drive error. The refusal still has to be
    // reported — just without detail, and without the whole page being read to decide that.
    [Fact]
    public async Task GetAccountInfo_OversizedErrorBody_IsNotParsedAndFallsBackToTheStatusCode()
    {
        var (client, handler) = CreateSut();

        // Valid JSON carrying a reason we would otherwise act on — skipped purely for its size.
        handler.Queue(HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":403,\"errors\":[{\"reason\":\"accessNotConfigured\"}],\"padding\":\""
            + new string('x', 32 * 1024)
            + "\"}}");

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.Equal("forbidden", ex.ErrorCode);
    }

    // A non-auth failure is transient and must stay an HttpRequestException: the validate handler LATCHES
    // the target on GoogleDriveAuthException, and Google having a bad minute must never do that.
    [Fact]
    public async Task GetAccountInfo_ServerError_StaysAnHttpRequestExceptionSoNothingLatches()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.BadGateway, "<html>oops</html>");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAccountInfoAsync("at", TestContext.Current.CancellationToken));

        Assert.IsNotType<GoogleDriveAuthException>(ex);
    }

    // --- Logging --------------------------------------------------------------------------------------

    // The diagnostic that was missing: with nothing logged on this path, the dialog text was the entire
    // evidence trail. Codes only, matching the OAuth client's posture — the bearer token must not leak
    // into a log line just because this body, unlike the OAuth ones, is safe to read.
    [Fact]
    public async Task GetAccountInfo_LogsTheReasonCodesAndNotTheAccessToken()
    {
        var handler = new FakeGoogleHttpHandler();
        handler.Queue(HttpStatusCode.Forbidden, """
            {"error":{"code":403,"message":"Google Drive API has not been used in project 12345.",
              "errors":[{"reason":"accessNotConfigured"}],"status":"PERMISSION_DENIED"}}
            """);

        var logger = new RecordingLogger<GoogleDriveAccountClient>();
        var client = new GoogleDriveAccountClient(new HttpClient(handler), logger);

        await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.GetAccountInfoAsync("ya29.super-secret-token", TestContext.Current.CancellationToken));

        var written = string.Join("\n", logger.Messages);

        Assert.Contains("403", written);
        Assert.Contains("accessNotConfigured", written);
        Assert.Contains("PERMISSION_DENIED", written);
        Assert.DoesNotContain("ya29.super-secret-token", written);
    }
}
