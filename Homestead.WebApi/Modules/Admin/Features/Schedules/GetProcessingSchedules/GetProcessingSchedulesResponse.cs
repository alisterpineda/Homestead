using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.GetProcessingSchedules;

public record GetProcessingSchedulesResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required ProcessingJobType JobType { get; init; }
    public int Priority { get; init; }
    public required string CronExpression { get; init; }
    public required string TimeZone { get; init; }
    public required bool IsEnabled { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public required DateTimeOffset NextRunAt { get; init; }
    public Guid? LastJobId { get; init; }
    public ProcessingJobStatus? LastRunStatus { get; init; }
}
