namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public record CreateProcessingScheduleResponse
{
    public required Guid Id { get; init; }
    public required DateTimeOffset NextRunAt { get; init; }
}
