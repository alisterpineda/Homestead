namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// Mutable operator state for one configured backup target. One row per <c>Backup:Targets:{key}</c>, but
/// created lazily — a target that has never been touched has no row and reads as enabled.
/// </summary>
/// <remarks>
/// Deliberately holds only what config cannot: whether the operator turned the target off, and whether
/// the delete breaker has latched. Last-run status is NOT here — it is derived from
/// <c>processing.jobs</c>, because a parent job's terminal transition happens inside the generic
/// <c>JobProcessorService.UpdateParentProgressAsync</c>, which offers no per-type hook, so a
/// handler-written status column could never be set reliably.
/// </remarks>
public class BackupTargetState
{
    /// <summary>The config key. Natural primary key — targets are named by config, not by surrogate id.</summary>
    public required string TargetKey { get; set; }

    /// <summary>False suspends scheduled and manual runs without deleting the config or the state rows.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When the delete breaker last latched. Null once an admin acknowledges it.</summary>
    public DateTimeOffset? PruneBlockedAt { get; set; }

    /// <summary>How many deletions were withheld when the breaker latched.</summary>
    public int? PruneBlockedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
