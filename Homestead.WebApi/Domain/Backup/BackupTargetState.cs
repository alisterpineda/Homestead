namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// Mutable operator state for one configured backup target. One row per <c>Backup:Targets:{key}</c>, but
/// created lazily — a target that has never been touched has no row and reads as enabled.
/// </summary>
/// <remarks>
/// Deliberately holds only what config cannot, and only what is true of the <i>destination</i> rather
/// than of any one dataset mirrored to it: whether the operator turned the whole target off. The delete
/// breaker latch lives on <see cref="BackupSource"/>, because a latch is a fact about one pairing's
/// change set. Last-run status is on neither — it is derived from <c>processing.jobs</c>, because a
/// parent job's terminal transition happens inside the generic
/// <c>JobProcessorService.UpdateParentProgressAsync</c>, which offers no per-type hook, so a
/// handler-written status column could never be set reliably.
/// </remarks>
public class BackupTargetState
{
    /// <summary>The config key. Natural primary key — targets are named by config, not by surrogate id.</summary>
    public required string TargetKey { get; set; }

    /// <summary>
    /// False suspends scheduled and manual runs for <i>every</i> source paired with this target, without
    /// deleting the config or any state rows.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
