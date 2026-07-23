using Homestead.WebApi.Modules.Documents.Backup;

namespace Homestead.WebApi.Modules.Documents.Processing;

/// <summary>Payload for the <c>BackupToTarget</c> planner job.</summary>
/// <param name="TargetKey">
/// The <c>Backup:Targets:{key}</c> to mirror to. Concrete and resolved at enqueue — there is no
/// "default target" chain, because a backup that silently retargeted would prune the destination the
/// operator actually named.
/// </param>
/// <param name="DryRun">Compute and report the change set without spawning any batch children.</param>
/// <param name="AllowLargePrune">Bypass the bulk-delete circuit breaker for this run only.</param>
public record BackupToTargetPayload(string TargetKey, bool DryRun = false, bool AllowLargePrune = false);

/// <summary>Payload for one <c>BackupBatch</c> child — a slice of the parent's fully-resolved plan.</summary>
public record BackupBatchPayload(string TargetKey, List<BackupAction> Actions);

/// <summary>
/// Staged planner output, written to <c>ProcessingJob.Result</c>. Read back by the admin page for both
/// dry runs (where it is the whole point) and real runs (where it explains what the children did).
/// </summary>
public record BackupRunSummary
{
    public required string TargetKey { get; init; }
    public bool DryRun { get; init; }

    /// <summary>True when the run did no work at all — see <see cref="SkipReason"/>.</summary>
    public bool Skipped { get; init; }

    public string? SkipReason { get; init; }

    public int PutCount { get; init; }
    public int MoveCount { get; init; }
    public int DeleteCount { get; init; }
    public int UnchangedCount { get; init; }
    public int BatchCount { get; init; }
    public long TotalBytes { get; init; }

    /// <summary>True when the delete breaker latched and deletions were withheld.</summary>
    public bool PruneBlocked { get; init; }

    public int BlockedDeleteCount { get; init; }
}
