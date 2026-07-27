using System.Collections.Concurrent;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// Hands out live Google access tokens for a target, refreshing and persisting behind a per-target lock.
/// </summary>
/// <remarks>
/// <para>
/// A singleton with an in-memory cache, because the alternative is a token refresh per HTTP request: a
/// batch of 25 uploads would otherwise burn 25 round-trips against Google's token endpoint before moving
/// a single byte, and the endpoint is rate-limited.
/// </para>
/// <para>
/// Singleton lifetime is also why the grant store is reached through <see cref="IServiceScopeFactory"/>
/// rather than injected — the store is scoped (it owns an <c>AppDbContext</c>), and capturing one in a
/// singleton would pin a single context for the life of the process.
/// </para>
/// </remarks>
public class GoogleDriveTokenManager(
    IServiceScopeFactory scopeFactory,
    IGoogleOAuthClient oauth,
    TimeProvider timeProvider,
    ILogger<GoogleDriveTokenManager> logger)
{
    /// <summary>
    /// Refresh this far ahead of the stated expiry. A token that is alive when a chunked upload starts
    /// must still be alive when its last chunk lands.
    /// </summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, GoogleTokenGrant> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Targets whose STORED access token has been rejected, so the next call must spend a refresh rather
    /// than hand the same credential back.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _rejected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a usable access token for the target, refreshing if the cached one is near expiry.
    /// </summary>
    /// <exception cref="GoogleDriveAuthException">
    /// The target is not connected, its grant will not decrypt, or Google rejected it — latched before
    /// this throws, so queued siblings short-circuit instead of asking again.
    /// </exception>
    public async Task<string> GetAccessTokenAsync(
        string targetKey, string clientId, string clientSecret, CancellationToken ct)
    {
        // Consumed once. A rejected token must not be served again from EITHER the cache or the row —
        // the whole point of the 401 retry is to arrive with a different credential.
        var rejected = _rejected.TryRemove(targetKey, out _);

        if (!rejected && TryGetFresh(targetKey, out var cached))
            return cached;

        var gate = _locks.GetOrAdd(targetKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Someone else refreshed while we queued — the common case when a batch's first few uploads
            // start together.
            if (!rejected && TryGetFresh(targetKey, out cached))
                return cached;

            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IGoogleDriveGrantStore>();

            var read = await store.ReadAsync(targetKey, ct);

            if (read.Status is GrantReadStatus.NotConnected)
                throw new GoogleDriveAuthException("not_connected",
                    $"Backup target '{targetKey}' is not connected to Google.");

            if (read.Status is GrantReadStatus.DecryptFailed)
                throw new GoogleDriveAuthException("decrypt_failed", GoogleDriveGrantStore.DecryptFailedReason);

            var stored = read.Grant!;

            // The stored access token may still be good — a process restart loses the cache, not the
            // token — so try it before spending a refresh. Unless Google has just rejected it, in which
            // case "not yet expired" is simply wrong.
            if (!rejected && stored.AccessTokenExpiresAt - RefreshMargin > timeProvider.GetUtcNow())
            {
                _cache[targetKey] = stored;
                return stored.AccessToken;
            }

            GoogleTokenGrant refreshed;
            try
            {
                refreshed = await oauth.RefreshAsync(clientId, clientSecret, stored.RefreshToken, ct);
            }
            catch (GoogleDriveAuthException ex)
            {
                // Persisted, not just thrown: from here the card reads unavailable-with-reason and every
                // other entry point refuses without touching Google.
                await store.LatchAuthFailureAsync(targetKey, ex.ErrorCode, ex.Message, ct);
                _cache.TryRemove(targetKey, out _);
                throw;
            }

            await store.UpdateTokensAsync(targetKey, refreshed, ct);
            _cache[targetKey] = refreshed;

            logger.LogDebug("Refreshed the Google access token for backup target {TargetKey}", targetKey);

            return refreshed.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Drops the cached token. Called on connect and disconnect — a stale token surviving a disconnect
    /// would keep writing into an account the operator just detached.
    /// </summary>
    public void Invalidate(string targetKey)
    {
        _cache.TryRemove(targetKey, out _);
        _rejected.TryRemove(targetKey, out _);
    }

    /// <summary>
    /// Records that the token just handed out was <i>rejected</i>, not merely stale.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Invalidate"/> because dropping the cache alone changes nothing: the
    /// stored grant's access token is usually still inside its stated lifetime, so the next call would
    /// read it off the row and present the identical rejected credential — making the 401 retry a
    /// guaranteed second failure.
    /// </remarks>
    public void InvalidateRejected(string targetKey)
    {
        _cache.TryRemove(targetKey, out _);
        _rejected[targetKey] = 0;
    }

    private bool TryGetFresh(string targetKey, out string accessToken)
    {
        if (_cache.TryGetValue(targetKey, out var grant)
            && grant.AccessTokenExpiresAt - RefreshMargin > timeProvider.GetUtcNow())
        {
            accessToken = grant.AccessToken;
            return true;
        }

        accessToken = string.Empty;
        return false;
    }
}
