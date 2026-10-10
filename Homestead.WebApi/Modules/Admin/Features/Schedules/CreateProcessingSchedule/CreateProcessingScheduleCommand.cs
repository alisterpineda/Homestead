using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public record CreateProcessingScheduleCommand
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required ProcessingJobType JobType { get; init; }
    public required string CronExpression { get; init; }
    public int Priority { get; init; }
    public bool IsEnabled { get; init; } = true;
}
