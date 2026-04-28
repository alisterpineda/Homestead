namespace Homestead.WebApi.Processing;

public enum ScheduleTickResult
{
    NothingDue,
    Fired,
    DisabledInvalidCron,
}

public interface IJobSchedulerControl
{
    Task<ScheduleTickResult> TickOnceAsync(CancellationToken ct = default);
}
