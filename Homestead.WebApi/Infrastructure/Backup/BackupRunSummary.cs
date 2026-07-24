namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Staged planner output, written to <c>ProcessingJob.Result</c> by every kind's planner. Read back by
/// the admin page for both dry runs (where it is the whole point) and real runs (where it explains what
/// the children did).
/// </summary>
/// <remarks>
/// Shared across kinds so the admin page needs no per-kind deserializer. Members are plain init-only:
/// this is deserialized from rows that may predate a shape change, so it must degrade rather than throw.
/// </remarks>
public record BackupRunSummary
{
    public Guid BackupSourceId { get; init; }

    public bool DryRun { get; init; }

    /// <summary>True when the run did no work at all — see <see cref="SkipReason"/>.</summary>
    public bool Skipped { get; init; }

    public string? SkipReason { get; init; }

    public int Puts { get; init; }
    public int Moves { get; init; }
    public int Deletes { get; init; }
    public int Unchanged { get; init; }
    public int BatchCount { get; init; }

    /// <summary>True when the delete breaker latched and deletions were withheld.</summary>
    public bool PruneBlocked { get; init; }

    public int BlockedDeleteCount { get; init; }

    /// <summary>
    /// Bytes this run will copy, or null when the kind cannot know at plan time — a watermark-style
    /// planner that regenerates content has no size until it generates it. Null renders as "—", never as
    /// "0 B", so an unknown is never mistaken for "nothing to copy".
    /// </summary>
    public long? TotalBytes { get; init; }
}
