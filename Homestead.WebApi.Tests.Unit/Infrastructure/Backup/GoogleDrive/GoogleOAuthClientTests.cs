using System.Net;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

public class GoogleOAuthClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static (GoogleOAuthClient Client, FakeGoogleHttpHandler Handler) CreateSut()
    {
        var handler = new FakeGoogleHttpHandler();
        var httpClient = new HttpClient(handler);
        var time = new FakeTimeProvider(Now);

        return (new GoogleOAuthClient(httpClient, time, NullLogger<GoogleOAuthClient>.Instance), handler);
    }

    // --- Device authorization -------------------------------------------------------------------------

    [Fact]
    public async Task StartDeviceAuthorization_RequestsOnlyTheDriveFileScope()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"device_code":"dc","user_code":"ABC-DEF","verification_url":"https://google.com/device",
             "expires_in":1800,"interval":5}
            """);

        var authorization = await client.StartDeviceAuthorizationAsync(
            "client-id", TestContext.Current.CancellationToken);

        Assert.Equal("dc", authorization.DeviceCode);
        Assert.Equal("ABC-DEF", authorization.UserCode);
        Assert.Equal("https://google.com/device", authorization.VerificationUrl);
        Assert.Equal(5, authorization.IntervalSeconds);
        Assert.Equal(Now.AddSeconds(1800), authorization.ExpiresAt);

        // The privacy claim the connect UI makes rests entirely on this being the only scope asked for.
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file", handler.RequestBodies[0]);
        Assert.DoesNotContain("drive.readonly", handler.RequestBodies[0]);
    }

    // Google sends verification_url; the RFC calls it verification_uri.
    [Fact]
    public async Task StartDeviceAuthorization_AcceptsTheRfcSpellingOfTheVerificationUrl()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"device_code":"dc","user_code":"ABC","verification_uri":"https://example.test/device"}
            """);

        var authorization = await client.StartDeviceAuthorizationAsync(
            "client-id", TestContext.Current.CancellationToken);

        Assert.Equal("https://example.test/device", authorization.VerificationUrl);
    }

    // The single most likely setup mistake: a Web application client cannot use the device flow, and
    // Google reports that as a SCOPE error, which sends people to the wrong place entirely.
    [Fact]
    public async Task StartDeviceAuthorization_InvalidScope_NamesTheClientType()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.BadRequest, """{"error":"invalid_scope"}""");

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.StartDeviceAuthorizationAsync("client-id", TestContext.Current.CancellationToken));

        Assert.Equal("invalid_scope", ex.ErrorCode);
        Assert.Contains("TVs & Limited Input devices", ex.Message);
    }

    [Fact]
    public async Task StartDeviceAuthorization_InvalidClient_PointsAtTheConfiguredId()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.StartDeviceAuthorizationAsync("client-id", TestContext.Current.CancellationToken));

        Assert.Equal("invalid_client", ex.ErrorCode);
        Assert.Contains("ClientId", ex.Message);
    }

    // --- Polling --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("authorization_pending", GoogleDevicePollStatus.Pending)]
    [InlineData("slow_down", GoogleDevicePollStatus.SlowDown)]
    [InlineData("expired_token", GoogleDevicePollStatus.Expired)]
    [InlineData("access_denied", GoogleDevicePollStatus.Denied)]
    public async Task PollDeviceToken_MapsEachOAuthErrorToItsStatus(string error, GoogleDevicePollStatus expected)
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}"}""");

        var result = await client.PollDeviceTokenAsync(
            "id", "secret", "dc", TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Grant);
    }

    [Fact]
    public async Task PollDeviceToken_Granted_ReturnsBothTokens()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""{"access_token":"at","refresh_token":"rt","expires_in":3600}""");

        var result = await client.PollDeviceTokenAsync(
            "id", "secret", "dc", TestContext.Current.CancellationToken);

        Assert.Equal(GoogleDevicePollStatus.Granted, result.Status);
        Assert.Equal("rt", result.Grant!.RefreshToken);
        Assert.Equal("at", result.Grant.AccessToken);

        // 60 s of slack, so a token that is alive when we check is still alive when the request it
        // authorizes reaches Google.
        Assert.Equal(Now.AddSeconds(3540), result.Grant.AccessTokenExpiresAt);

        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code",
            handler.RequestBodies[0]);
    }

    // An hour-long credential with no refresh token reads as connected and dies overnight — worse than
    // a visible failure, for something whose whole job is running unattended weeks later.
    [Fact]
    public async Task PollDeviceToken_GrantedWithoutARefreshToken_Throws()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""{"access_token":"at","expires_in":3600}""");

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.PollDeviceTokenAsync("id", "secret", "dc", TestContext.Current.CancellationToken));

        Assert.Equal("malformed_response", ex.ErrorCode);
    }

    // Google renders Drive access as a TICKBOX on the consent screen. Approving without ticking it issues
    // a valid token carrying no Drive scope, and the flow looks successful from both ends — so it is worth
    // failing here, with the fix named, rather than at the first Drive call as an opaque 403.
    [Fact]
    public async Task PollDeviceToken_GrantedWithoutTheDriveScope_PointsAtTheConsentTickbox()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"access_token":"at","refresh_token":"rt","expires_in":3600,
             "scope":"https://www.googleapis.com/auth/userinfo.email"}
            """);

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.PollDeviceTokenAsync("id", "secret", "dc", TestContext.Current.CancellationToken));

        Assert.Equal("insufficient_scope", ex.ErrorCode);
        Assert.Contains("checkbox", ex.Message);
    }

    // The rejected authorization is live whether we want it or not: Google does not invalidate it, and it
    // remembers the consent decision — so an un-revoked one both strands a credential in the user's
    // security page and can be handed straight back on the retry, without the checkbox ever reappearing.
    [Fact]
    public async Task PollDeviceToken_GrantedWithoutTheDriveScope_HandsTheUselessGrantBack()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"access_token":"at","refresh_token":"rt","expires_in":3600,
             "scope":"https://www.googleapis.com/auth/userinfo.email"}
            """);

        await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.PollDeviceTokenAsync("id", "secret", "dc", TestContext.Current.CancellationToken));

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("revoke", handler.RequestUrls[1]);
        Assert.Contains("token=rt", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task PollDeviceToken_GrantedWithTheDriveScopeAmongOthers_Succeeds()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"access_token":"at","refresh_token":"rt","expires_in":3600,
             "scope":"https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/userinfo.email"}
            """);

        var result = await client.PollDeviceTokenAsync(
            "id", "secret", "dc", TestContext.Current.CancellationToken);

        Assert.Equal(GoogleDevicePollStatus.Granted, result.Status);
    }

    // Full `drive` strictly subsumes `drive.file`, so an account that granted it earlier can have it come
    // back here instead. Refusing that would block a connection that works perfectly.
    [Fact]
    public async Task PollDeviceToken_GrantedABroaderDriveScope_IsAccepted()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""
            {"access_token":"at","refresh_token":"rt","expires_in":3600,
             "scope":"https://www.googleapis.com/auth/drive"}
            """);

        var result = await client.PollDeviceTokenAsync(
            "id", "secret", "dc", TestContext.Current.CancellationToken);

        Assert.Equal(GoogleDevicePollStatus.Granted, result.Status);
    }

    // Three-valued on purpose: the caller LATCHES the target as disconnected on this exception type, so a
    // response that simply does not state a scope must not be read as a shortfall. A Google response-shape
    // change would otherwise refuse every new connection.
    [Fact]
    public async Task PollDeviceToken_GrantedWithNoStatedScope_IsAcceptedRatherThanAssumedInsufficient()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""{"access_token":"at","refresh_token":"rt","expires_in":3600}""");

        var result = await client.PollDeviceTokenAsync(
            "id", "secret", "dc", TestContext.Current.CancellationToken);

        Assert.Equal(GoogleDevicePollStatus.Granted, result.Status);
    }

    // --- Refresh --------------------------------------------------------------------------------------

    [Fact]
    public async Task Refresh_CarriesTheExistingRefreshTokenForwardWhenGoogleSendsNone()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""{"access_token":"fresh","expires_in":3600}""");

        var grant = await client.RefreshAsync(
            "id", "secret", "stored-rt", TestContext.Current.CancellationToken);

        // Google does not rotate refresh tokens on an ordinary refresh; dropping ours would disconnect
        // the target on its first successful refresh.
        Assert.Equal("stored-rt", grant.RefreshToken);
        Assert.Equal("fresh", grant.AccessToken);
    }

    [Fact]
    public async Task Refresh_AdoptsARotatedRefreshTokenWhenOneComesBack()
    {
        var (client, handler) = CreateSut();
        handler.QueueOk("""{"access_token":"fresh","refresh_token":"rotated","expires_in":3600}""");

        var grant = await client.RefreshAsync(
            "id", "secret", "stored-rt", TestContext.Current.CancellationToken);

        Assert.Equal("rotated", grant.RefreshToken);
    }

    [Fact]
    public async Task Refresh_InvalidGrant_ThrowsTheTypedAuthException()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.RefreshAsync("id", "secret", "rt", TestContext.Current.CancellationToken));

        Assert.Equal("invalid_grant", ex.ErrorCode);
    }

    // The distinction the whole type exists for: the caller LATCHES the target as disconnected on
    // GoogleDriveAuthException, and one bad minute of connectivity must never do that.
    [Fact]
    public async Task Refresh_NetworkFailure_StaysAnHttpRequestExceptionSoNothingLatches()
    {
        var (client, handler) = CreateSut();
        handler.Throw = new HttpRequestException("connection reset");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.RefreshAsync("id", "secret", "rt", TestContext.Current.CancellationToken));

        Assert.IsNotType<GoogleDriveAuthException>(ex);
    }

    [Fact]
    public async Task Refresh_ServerError_StaysAnHttpRequestExceptionSoNothingLatches()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.BadGateway, "<html>oops</html>");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.RefreshAsync("id", "secret", "rt", TestContext.Current.CancellationToken));
    }

    // --- Revoke ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Revoke_SwallowsANonSuccessResponse()
    {
        var (client, handler) = CreateSut();
        handler.Queue(HttpStatusCode.BadRequest, """{"error":"invalid_token"}""");

        // A revocation we cannot deliver must not block the disconnect — otherwise a target whose config
        // vanished could never be cleaned up.
        await client.RevokeAsync("rt", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Revoke_SwallowsANetworkFailure()
    {
        var (client, handler) = CreateSut();
        handler.Throw = new HttpRequestException("no route to host");

        await client.RevokeAsync("rt", TestContext.Current.CancellationToken);
    }

    // --- Logging --------------------------------------------------------------------------------------

    // Every one of these bodies carries a credential or a device code. The client logs OAuth error CODES
    // and nothing else; this asserts the rule at the one place it could plausibly slip.
    [Fact]
    public async Task Refresh_NeverLogsTheResponseBody()
    {
        var handler = new FakeGoogleHttpHandler();
        handler.Queue(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");

        var logger = new RecordingLogger<GoogleOAuthClient>();
        var client = new GoogleOAuthClient(
            new HttpClient(handler), new FakeTimeProvider(Now), logger);

        await Assert.ThrowsAsync<GoogleDriveAuthException>(() =>
            client.RefreshAsync("id", "super-secret-value", "rt", TestContext.Current.CancellationToken));

        var written = string.Join("\n", logger.Messages);

        Assert.Contains("invalid_grant", written);
        Assert.DoesNotContain("error_description", written);
        Assert.DoesNotContain("super-secret-value", written);
        Assert.DoesNotContain("rt", written.Replace("invalid_grant", string.Empty));
    }
}
