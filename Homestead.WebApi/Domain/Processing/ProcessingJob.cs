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

    public T? GetPayload<T>() => Payload is null ? default : JsonSerializer.Deserialize<T>(Payload);
    public void SetPayload<T>(T value) => Payload = JsonSerializer.Serialize(value);
}
