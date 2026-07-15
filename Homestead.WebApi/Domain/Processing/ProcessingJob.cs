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

    public T? GetPayload<T>() => Payload is null ? default : JsonSerializer.Deserialize<T>(Payload);
    public void SetPayload<T>(T value) => Payload = JsonSerializer.Serialize(value);

    public T? GetResult<T>() => Result is null ? default : JsonSerializer.Deserialize<T>(Result);
    public void SetResult<T>(T value) => Result = JsonSerializer.Serialize(value);
}
