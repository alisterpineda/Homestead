namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <summary>Why a read of the stored grant did not produce one.</summary>
public enum GrantReadStatus
{
    Ok,

    /// <summary>No grant has ever been stored for this target, or it was disconnected.</summary>
    NotConnected,

    /// <summary>
    /// A grant is stored but will not decrypt. Its own status because it has its own cause and its own
    /// fix: self-hosters routinely recreate containers without persisting the Data Protection key ring,
    /// and telling them Google revoked their access would send them to the wrong place entirely.
    /// </summary>
    DecryptFailed
}

/// <param name="Grant">Populated only when <paramref name="Status"/> is <see cref="GrantReadStatus.Ok"/>.</param>
/// <param name="AuthFailedReason">
/// The persisted latch reason, if one is set. Present alongside a perfectly readable grant: a latched
/// target still has its credential on file, it just knows the credential is dead.
/// </param>
public readonly record struct GrantReadResult(
    GrantReadStatus Status,
    GoogleTokenGrant? Grant = null,
    string? ConnectedAccountEmail = null,
    DateTimeOffset? GrantIssuedAt = null,
    DateTimeOffset? AuthFailedAt = null,
    string? AuthFailedReason = null);

/// <summary>
/// Owns the stored Google grant for a target: encryption, the auth-failure latch, and the lazy creation
/// of the target-state row that carries both.
/// </summary>
public interface IGoogleDriveGrantStore
{
    Task<GrantReadResult> ReadAsync(string targetKey, CancellationToken ct);

    /// <summary>Stores (or replaces) the grant, stamps the issue time, and clears any latch.</summary>
    Task SaveAsync(string targetKey, GoogleTokenGrant grant, string? connectedAccountEmail, CancellationToken ct);

    /// <summary>
    /// Replaces the access token half of a stored grant after a refresh, leaving email, issue time, and
    /// latch state alone. Distinct from <see cref="SaveAsync"/>: a routine refresh is not a reconnection,
    /// and must not reset the seven-day heuristic's clock.
    /// </summary>
    Task UpdateTokensAsync(string targetKey, GoogleTokenGrant grant, CancellationToken ct);

    /// <summary>Forgets the grant and the latch. The row survives — the enable toggle lives on it.</summary>
    Task ClearAsync(string targetKey, CancellationToken ct);

    /// <summary>Records that the grant is dead, with a reason built for a human to act on.</summary>
    Task LatchAuthFailureAsync(string targetKey, string errorCode, string message, CancellationToken ct);

    /// <summary>Clears the latch after a probe or operation proved the grant works.</summary>
    Task ClearAuthFailureAsync(string targetKey, CancellationToken ct);
}
