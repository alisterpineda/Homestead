namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Derives "how is this pairing actually doing" from <c>processing.jobs</c>, for every surface that asks.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the admin backup page rather than duplicated beside it. Two surfaces reading the same
/// tables through two private copies of the derivation is how they end up disagreeing about whether a
/// backup is healthy — and the per-document panel hangs a liveness claim on exactly these values, so it
/// has to inherit the same vocabulary and the same bugs as <c>/admin/backup</c>, not a second set.
/// </para>
/// <para>
/// Every member reads and nothing writes: the run status is a fact about job rows, never a column any
/// handler maintains.
/// </para>
/// </remarks>
public interface IBackupRunHistory
{
    /// <summary>The newest planner run per pairing, whatever its outcome.</summary>
    Task<Dictionary<Guid, BackupRunStatus>> GetLastRunsAsync(
        IReadOnlyList<Guid> sourceIds, CancellationToken ct);

    /// <summary>
    /// When each pairing last finished a run that actually mirrored something. Excludes skipped and dry
    /// runs, both of which complete normally while copying nothing.
    /// </summary>
    Task<Dictionary<Guid, DateTimeOffset>> GetLastSuccessfulRunsAsync(
        IReadOnlyList<Guid> sourceIds, CancellationToken ct);

    /// <summary>The schedule rows behind the given <c>BackupSource.ScheduleId</c> values, keyed by id.</summary>
    Task<Dictionary<Guid, BackupScheduleSummary>> GetSchedulesAsync(
        IReadOnlyList<Guid> scheduleIds, CancellationToken ct);

    /// <summary>
    /// The typed per-action failures staged by one run's failed batch children.
    /// </summary>
    /// <remarks>
    /// Takes the parent job id straight off <see cref="BackupRunStatus.JobId"/>, so a caller that already
    /// has the run needs no second newest-parent query — and callers should only ask when that run
    /// reports failed children, since otherwise the answer is known to be empty. An older row that staged
    /// no failure list simply contributes nothing: absence here means "not recorded", never "succeeded".
    /// </remarks>
    Task<IReadOnlyList<BackupActionFailure>> GetActionFailuresAsync(
        Guid parentJobId, CancellationToken ct);
}
