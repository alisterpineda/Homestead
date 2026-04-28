using System.Text.Json;

namespace Homestead.WebApi.Domain.Processing;

public class ProcessingSchedule
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ProcessingJobType JobType { get; set; }
    public string? Payload { get; set; }
    public int Priority { get; set; }
    public required string CronExpression { get; set; }
    public string TimeZone { get; set; } = "Etc/UTC";
    public bool IsEnabled { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset NextRunAt { get; set; }
    public Guid? LastJobId { get; set; }

    public T? GetPayload<T>() => Payload is null ? default : JsonSerializer.Deserialize<T>(Payload);
    public void SetPayload<T>(T value) => Payload = JsonSerializer.Serialize(value);
}
