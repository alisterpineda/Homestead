using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

public class GoogleDriveTokenManagerTests : IDisposable
{
    private const string TargetKey = "gdrive";
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IGoogleDriveGrantStore _store = Substitute.For<IGoogleDriveGrantStore>();
    private readonly ServiceProvider _services;
    private readonly GoogleDriveTokenManager _sut;

    public GoogleDriveTokenManagerTests()
    {
        // The manager is a singleton and reaches the scoped store through a scope factory, so the test
        // has to hand it a real container rather than the substitute directly.
        _services = new ServiceCollection()
            .AddScoped(_ => _store)
            .BuildServiceProvider();

        _sut = new GoogleDriveTokenManager(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _oauth,
            _time,
            NullLogger<GoogleDriveTokenManager>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private void StoredGrant(string accessToken, DateTimeOffset expiresAt, string refreshToken = "rt") =>
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(
                GrantReadStatus.Ok, new GoogleTokenGrant(refreshToken, accessToken, expiresAt)));

    private Task<string> GetTokenAsync() =>
        _sut.GetAccessTokenAsync(TargetKey, "id", "secret", TestContext.Current.CancellationToken);

    // --- Caching --------------------------------------------------------------------------------------

    // A batch of 25 uploads must not burn 25 round trips against a rate-limited token endpoint before
    // moving a single byte.
    [Fact]
    public async Task RepeatedCalls_HitTheStoreAndGoogleOnlyOnce()
    {
        StoredGrant("cached", Now.AddHours(1));

        Assert.Equal("cached", await GetTokenAsync());
        Assert.Equal("cached", await GetTokenAsync());
        Assert.Equal("cached", await GetTokenAsync());

        await _store.Received(1).ReadAsync(TargetKey, Arg.Any<CancellationToken>());
        await _oauth.DidNotReceive().RefreshAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // A process restart loses the cache but not the token; spending a refresh to rediscover a token that
    // is still good is pure waste.
    [Fact]
    public async Task AStoredTokenThatIsStillFresh_IsUsedWithoutRefreshing()
    {
        StoredGrant("still-good", Now.AddMinutes(30));

        Assert.Equal("still-good", await GetTokenAsync());

        await _oauth.DidNotReceive().RefreshAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Refresh --------------------------------------------------------------------------------------

    [Fact]
    public async Task ATokenInsideTheRefreshMargin_IsRefreshedAndPersisted()
    {
        // Inside the two-minute margin: technically alive, but a chunked upload started now could outlive
        // it mid-flight.
        StoredGrant("stale", Now.AddSeconds(90));

        var refreshed = new GoogleTokenGrant("rt", "fresh", Now.AddHours(1));
        _oauth.RefreshAsync("id", "secret", "rt", Arg.Any<CancellationToken>()).Returns(refreshed);

        Assert.Equal("fresh", await GetTokenAsync());

        await _store.Received(1).UpdateTokensAsync(TargetKey, refreshed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterARefresh_TheNewTokenIsServedFromCache()
    {
        StoredGrant("stale", Now.AddSeconds(30));
        _oauth.RefreshAsync("id", "secret", "rt", Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenGrant("rt", "fresh", Now.AddHours(1)));

        await GetTokenAsync();
        Assert.Equal("fresh", await GetTokenAsync());

        await _oauth.Received(1).RefreshAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Latching -------------------------------------------------------------------------------------

    [Fact]
    public async Task InvalidGrant_LatchesTheTargetAndRethrows()
    {
        StoredGrant("stale", Now.AddSeconds(30));
        _oauth.RefreshAsync("id", "secret", "rt", Arg.Any<CancellationToken>())
            .Returns<GoogleTokenGrant>(_ =>
                throw new GoogleDriveAuthException("invalid_grant", "revoked"));

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(GetTokenAsync);

        Assert.Equal("invalid_grant", ex.ErrorCode);
        await _store.Received(1).LatchAuthFailureAsync(
            TargetKey, "invalid_grant", "revoked", Arg.Any<CancellationToken>());
    }

    // The distinction the typed exception exists for: one bad minute of connectivity must never mark a
    // working backup as permanently disconnected.
    [Fact]
    public async Task ATransientHttpFailure_DoesNotLatch()
    {
        StoredGrant("stale", Now.AddSeconds(30));
        _oauth.RefreshAsync("id", "secret", "rt", Arg.Any<CancellationToken>())
            .Returns<GoogleTokenGrant>(_ => throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<HttpRequestException>(GetTokenAsync);

        await _store.DidNotReceive().LatchAuthFailureAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotConnected_ThrowsWithoutTouchingGoogle()
    {
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(GrantReadStatus.NotConnected));

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(GetTokenAsync);

        Assert.Equal("not_connected", ex.ErrorCode);
        await _oauth.DidNotReceive().RefreshAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecryptFailure_ReportsItsOwnReason()
    {
        _store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(GrantReadStatus.DecryptFailed));

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(GetTokenAsync);

        Assert.Equal("decrypt_failed", ex.ErrorCode);
        Assert.Contains("data-protection keys changed", ex.Message);
    }

    // --- Invalidation ---------------------------------------------------------------------------------

    // A token cached under the old grant would keep writing into an account the operator just detached.
    [Fact]
    public async Task Invalidate_ForcesTheNextCallBackToTheStore()
    {
        StoredGrant("first", Now.AddHours(1));
        Assert.Equal("first", await GetTokenAsync());

        _sut.Invalidate(TargetKey);
        StoredGrant("second", Now.AddHours(1));

        Assert.Equal("second", await GetTokenAsync());
        await _store.Received(2).ReadAsync(TargetKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedRefresh_EvictsTheCacheSoTheNextCallReReads()
    {
        StoredGrant("stale", Now.AddSeconds(30));
        _oauth.RefreshAsync("id", "secret", "rt", Arg.Any<CancellationToken>())
            .Returns<GoogleTokenGrant>(_ => throw new GoogleDriveAuthException("invalid_grant", "revoked"));

        await Assert.ThrowsAsync<GoogleDriveAuthException>(GetTokenAsync);
        await Assert.ThrowsAsync<GoogleDriveAuthException>(GetTokenAsync);

        await _store.Received(2).ReadAsync(TargetKey, Arg.Any<CancellationToken>());
    }
}
