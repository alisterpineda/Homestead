using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// What one module offers up as backup-able. Code declares the kinds that <i>can</i> be mirrored; a
/// <c>backup.sources</c> row can only select from what is registered here — the same relationship
/// <c>ProcessingSchedule</c> has with <c>ProcessingJobType</c>.
/// </summary>
/// <remarks>
/// This is also where per-kind behaviour lands as the feature grows (a mirrored-totals query, a
/// force-sync filter), so that Admin stays generic: the admin surface must never grow a switch on
/// <see cref="Key"/>.
/// </remarks>
public interface IBackupSourceDescriptor
{
    /// <summary>Stable identifier persisted in <c>backup.sources.source_kind</c> (e.g. <c>documents</c>).</summary>
    string Key { get; }

    /// <summary>Human-readable label for the admin picker and source rows.</summary>
    string DisplayName { get; }

    /// <summary>
    /// The planner job enqueued for a run of this kind. Per-kind rather than one generic dispatcher: each
    /// kind's planner has its own handler, and the job list stays readable.
    /// </summary>
    ProcessingJobType JobType { get; }

    /// <summary>
    /// The top-level folder this kind owns at the target root (<c>&lt;root&gt;/Documents/…</c>). Namespacing
    /// is what lets several kinds share one target without their prunes colliding.
    /// </summary>
    string RootFolder { get; }

    /// <summary>
    /// Whether runs of this kind can delete from the mirror, and therefore whether the bulk-delete
    /// breaker's affordances (latch banner, "prune anyway") mean anything for it.
    /// </summary>
    bool SupportsPrune { get; }
}
