using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The schedule driving one pairing's recurring runs, as both backup surfaces render it.
/// </summary>
/// <remarks>
/// Lives beside <see cref="BackupRunStatus"/> rather than in a feature folder because
/// <see cref="IBackupRunHistory"/> returns it, and a shared derivation that returned a slice-owned type
/// would put Infrastructure downstream of one of its own callers.
/// </remarks>
public record BackupScheduleSummary
{
    public required Guid Id { get; init; }
    public required string CronExpression { get; init; }
    public required bool IsEnabled { get; init; }
    public required int Priority { get; init; }
    public required DateTimeOffset NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
}

/// <summary>
/// The most recent planner job for one pairing, read straight off <c>processing.jobs</c>.
/// </summary>
/// <remarks>
/// Derived rather than denormalized onto the source row on purpose: a parent job's terminal transition
/// happens inside the generic <c>JobProcessorService.UpdateParentProgressAsync</c>, which offers no
/// per-type hook, so a handler-written status column could never be set reliably. Reading the job row
/// sidesteps the race entirely and keeps the tables to genuine state.
/// </remarks>
public record BackupRunStatus
{
    public required Guid JobId { get; init; }
    public required ProcessingJobStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>The parent's own failure message — set only when the planner itself threw.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Failure messages from the run's failed batch children, capped. This is where a per-file failure
    /// actually lives: a parent whose children failed carries no <see cref="Error"/> of its own, because
    /// the generic parent-progress logic derives status from the child tally without writing one.
    /// </summary>
    public IReadOnlyList<string> BatchErrors { get; init; } = [];

    public required int TotalChildCount { get; init; }
    public required int CompletedChildCount { get; init; }
    public required int FailedChildCount { get; init; }

    /// <summary>
    /// True when any batch in the run reported the destination full. A typed field, not a parsed message:
    /// the card renders one rolled-up sentence instead of a stack of identical batch errors.
    /// </summary>
    public required bool OutOfSpace { get; init; }

    /// <summary>The planner's staged change-set summary. Populated for dry runs and real runs alike.</summary>
    public BackupRunSummary? Summary { get; init; }
}
