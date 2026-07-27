using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Stands in for Google's OAuth endpoints so the connect flow is exercised end to end — real endpoints,
/// real Data Protection, real database — without a live Google client or a human at google.com/device.
/// </summary>
/// <remarks>
/// A singleton so a test can script the next poll result and then read back what was revoked. The suite
/// runs serially in one collection, so shared mutable state is safe here; <see cref="Reset"/> puts it
/// back to the happy path between tests.
/// </remarks>
public sealed class FakeGoogleOAuthClient : IGoogleOAuthClient
{
    public const string DeviceCode = "fake-device-code";
    public const string UserCode = "ABCD-EFGH";

    /// <summary>What the next poll answers with. Defaults to an immediate grant.</summary>
    public GoogleDevicePollResult NextPollResult { get; set; } = Granted("refresh-token-1");

    /// <summary>Set to make a refresh fail as a dead grant rather than succeed.</summary>
    public GoogleDriveAuthException? RefreshFailure { get; set; }

    /// <summary>Set to make starting the device flow fail the way a bad OAuth client does.</summary>
    public GoogleDriveAuthException? StartFailure { get; set; }

    public List<string> RevokedTokens { get; } = [];

    public static GoogleDevicePollResult Granted(string refreshToken) =>
        new(GoogleDevicePollStatus.Granted,
            new GoogleTokenGrant(refreshToken, "access-token", DateTimeOffset.UtcNow.AddHours(1)));

    public void Reset()
    {
        NextPollResult = Granted("refresh-token-1");
        RefreshFailure = null;
        StartFailure = null;
        RevokedTokens.Clear();
    }

    public Task<GoogleDeviceAuthorization> StartDeviceAuthorizationAsync(
        string clientId, CancellationToken ct) =>
        StartFailure is { } failure
            ? Task.FromException<GoogleDeviceAuthorization>(failure)
            : Task.FromResult(new GoogleDeviceAuthorization(
                DeviceCode, UserCode, "https://www.google.com/device", 5,
                DateTimeOffset.UtcNow.AddMinutes(30)));

    public Task<GoogleDevicePollResult> PollDeviceTokenAsync(
        string clientId, string clientSecret, string deviceCode, CancellationToken ct) =>
        Task.FromResult(NextPollResult);

    public Task<GoogleTokenGrant> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken ct) =>
        RefreshFailure is { } failure
            ? Task.FromException<GoogleTokenGrant>(failure)
            : Task.FromResult(new GoogleTokenGrant(
                refreshToken, "access-token", DateTimeOffset.UtcNow.AddHours(1)));

    public Task RevokeAsync(string token, CancellationToken ct)
    {
        RevokedTokens.Add(token);
        return Task.CompletedTask;
    }
}

/// <summary>Stands in for Drive's <c>about.get</c>, which the connect and validate paths both read.</summary>
public sealed class FakeGoogleDriveAccountClient : IGoogleDriveAccountClient
{
    public GoogleDriveAccountInfo NextAccount { get; set; } = new("owner@example.test", 1_000, 15_000_000_000);

    /// <summary>Set to make the validate probe fail the way a revoked grant does.</summary>
    public Exception? Failure { get; set; }

    public void Reset()
    {
        NextAccount = new GoogleDriveAccountInfo("owner@example.test", 1_000, 15_000_000_000);
        Failure = null;
    }

    public Task<GoogleDriveAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct) =>
        Failure is { } failure
            ? Task.FromException<GoogleDriveAccountInfo>(failure)
            : Task.FromResult(NextAccount);
}
