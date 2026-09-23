using System.Text.Json;

namespace Homestead.WebApi.Domain.Processing;

public class ProcessingJob
{
    public Guid Id { get; set; }
    public ProcessingJobType Type { get; set; }
    public ProcessingJobStatus Status { get; set; }
    public ProcessingEntityType? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Payload { get; set; }

    // Staged handler output (JSON). Written by handlers that produce a durable result for a follow-up
    // read (e.g. statement-parse staging, read back by GET /finances/imports/{jobId}). Nothing here
    // touches the ledger until an explicit commit. Distinct from Error (failure detail).
    public string? Result { get; set; }

    public string? Error { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Guid? ParentJobId { get; set; }
    public int TotalChildCount { get; set; }
    public int CompletedChildCount { get; set; }
    public int FailedChildCount { get; set; }

    public Guid? ScheduleId { get; set; }

    /// <summary>How many runs have already ended in an exception. A lease reclaim is not a run that ended.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// The handler's <c>IJobHandler.MaxAttempts</c> as of its last run. Written by the processor, never at
    /// enqueue: children are inserted straight into the table by their parent handlers, so an enqueue-time
    /// stamp would miss exactly the jobs most worth retrying. Informational — the live handler decides.
    /// </summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>
    /// Earliest time the job may be claimed; null means now. A retry pushes this forward by the backoff.
    /// Nullable rather than defaulted so every insert path — <c>JobQueue</c> and the parent handlers that
    /// add children directly — stays correct without knowing the column exists.
    /// </summary>
    public DateTimeOffset? RunAfter { get; set; }

    /// <summary>
    /// Lease expiry while a runner holds the job; null when nothing does. A childless row left
    /// <c>InProgress</c> with an expired lease is what a crashed process looks like, and the ordinary claim
    /// picks it back up. A parent waiting on its children holds no lease, which is what keeps it from ever
    /// being reclaimed.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public T? GetPayload<T>() => Payload is null ? default : JsonSerializer.Deserialize<T>(Payload);
    public void SetPayload<T>(T value) => Payload = JsonSerializer.Serialize(value);

    public T? GetResult<T>() => Result is null ? default : JsonSerializer.Deserialize<T>(Result);
    public void SetResult<T>(T value) => Result = JsonSerializer.Serialize(value);
}
