using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// One backup pairing: "mirror &lt;source kind&gt; to &lt;target&gt;". The central operational entity — code
/// registers the source kinds that <i>can</i> be backed up and config declares the targets that
/// <i>exist</i>, but which pairings are actually live, whether they are enabled, when they run, and
/// whether the delete breaker has latched are all admin decisions, so they live here as rows.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unique index on <c>(SourceKind, lower(TargetKey))</c> is load-bearing for mirror correctness,
/// not tidiness.</b> Two rows of the same kind on one target would share a
/// <c>IBackupSourceDescriptor.RootFolder</c> and therefore the same destination paths, while each keeps
/// its own state rows — so every run of one would see the other's files as unknown and prune them by
/// anti-join, and the two would punch holes in each other's mirrors indefinitely. Allowing scoped
/// backups later (two rows, same kind, same target) requires a per-row folder/scope column first, with
/// the catalog's RootFolder demoted to a default.
/// </para>
/// <para>
/// Neither <see cref="SourceKind"/> nor <see cref="TargetKey"/> can carry a foreign key: the first names
/// a code-registered descriptor and the second a config section. A row whose kind or target has since
/// disappeared is not deleted or hidden — it renders unavailable-with-reason on the admin page, because
/// silently dropping a pairing would also silently stop backing something up.
/// </para>
/// </remarks>
public class BackupSource
{
    /// <summary>
    /// Identity of the pairing, and directly the <c>ProcessingJob.EntityId</c> of every run and batch for
    /// it — there is no derivation step (the config-keyed target this replaced needed a
    /// <c>DeterministicGuid</c>; a row does not).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>The <c>IBackupSourceDescriptor.Key</c> being mirrored (e.g. <c>documents</c>).</summary>
    public required string SourceKind { get; set; }

    /// <summary>
    /// The <c>Backup:Targets:{key}</c> mirrored to, in the resolver's canonical casing. Writers persist
    /// the resolved target's own Key rather than what the caller typed, so the row and config agree.
    /// </summary>
    public required string TargetKey { get; set; }

    /// <summary>False suspends scheduled and manual runs for this pairing without deleting its state.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The <see cref="ProcessingSchedule"/> driving recurring runs, or null for manual-only. Unique — one
    /// schedule per pairing — and <c>ON DELETE SET NULL</c>, so deleting the schedule row out of band
    /// (via /admin/schedules) degrades the pairing to manual rather than orphaning a dangling id.
    /// </summary>
    public Guid? ScheduleId { get; set; }

    /// <summary>
    /// When the delete breaker last latched for this pairing. Null once an admin acknowledges it. Lives
    /// here rather than on the target: a latch is a fact about one dataset's change set, and clearing it
    /// on the target would dismiss every kind's warning at once.
    /// </summary>
    public DateTimeOffset? PruneBlockedAt { get; set; }

    /// <summary>How many deletions were withheld when the breaker latched.</summary>
    public int? PruneBlockedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
