using Cronos;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Processing;

public class JobSchedulerService(
    IServiceScopeFactory scopeFactory,
    IOptions<JobSchedulingOptions> options,
    TimeProvider timeProvider,
    ILogger<JobSchedulerService> logger) : BackgroundService, IJobSchedulerControl
{
    private readonly TimeSpan _pollInterval =
        TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Job scheduler is disabled via configuration");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in scheduler loop");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DrainDueSchedulesAsync(CancellationToken ct)
    {
        // Drain everything that is currently due before sleeping again.
        while (!ct.IsCancellationRequested)
        {
            var result = await TickOnceAsync(ct);
            if (result == ScheduleTickResult.NothingDue) break;
        }
    }

    /// <summary>
    /// Processes the single most overdue enabled schedule. On a successful claim, enqueues a
    /// corresponding job and signals the worker. Distinguishes "fired", "disabled invalid cron",
    /// and "nothing due" so callers (drain loop, tests, future admin actions) can react precisely.
    /// </summary>
    public async Task<ScheduleTickResult> TickOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = timeProvider.GetUtcNow();

        var candidate = await db.ProcessingSchedules
            .Where(s => s.IsEnabled && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .Select(s => new
            {
                s.Id,
                s.NextRunAt,
                s.CronExpression,
                s.TimeZone,
                s.JobType,
                s.Payload,
                s.Priority,
                s.CreatedByUserId,
            })
            .FirstOrDefaultAsync(ct);

        if (candidate is null) return ScheduleTickResult.NothingDue;

        DateTimeOffset nextOccurrence;
        try
        {
            nextOccurrence = ComputeNextOccurrence(candidate.CronExpression, candidate.TimeZone, now);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Schedule {ScheduleId} has invalid cron expression {Cron}; disabling",
                candidate.Id, candidate.CronExpression);

            await db.ProcessingSchedules
                .Where(s => s.Id == candidate.Id)
                .ExecuteUpdateAsync(setter => setter.SetProperty(s => s.IsEnabled, false), ct);

            return ScheduleTickResult.DisabledInvalidCron;
        }

        // Atomic claim: only the caller that still sees the schedule with the originally-observed
        // NextRunAt and IsEnabled=true wins. Concurrent ticks see 0 rows updated and skip.
        var claimed = await db.ProcessingSchedules
            .Where(s => s.Id == candidate.Id
                        && s.IsEnabled
                        && s.NextRunAt == candidate.NextRunAt)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(s => s.LastRunAt, now)
                .SetProperty(s => s.NextRunAt, nextOccurrence), ct);

        if (claimed == 0) return ScheduleTickResult.NothingDue;

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var jobId = await queue.EnqueueAsync(candidate.JobType, opts =>
        {
            opts.Payload = candidate.Payload;
            opts.Priority = candidate.Priority;
            opts.CreatedByUserId = candidate.CreatedByUserId;
            opts.ScheduleId = candidate.Id;
        }, ct);

        // Best-effort denormalisation of the latest spawned job. If this fails we still
        // ran the schedule successfully; the job remains discoverable via ScheduleId on the job row.
        await db.ProcessingSchedules
            .Where(s => s.Id == candidate.Id)
            .ExecuteUpdateAsync(setter => setter.SetProperty(s => s.LastJobId, (Guid?)jobId), ct);

        logger.LogInformation(
            "Schedule {ScheduleId} fired job {JobId} ({JobType}); next run at {NextRunAt}",
            candidate.Id, jobId, candidate.JobType, nextOccurrence);

        return ScheduleTickResult.Fired;
    }

    private static DateTimeOffset ComputeNextOccurrence(string cronExpression, string timeZoneId, DateTimeOffset from)
    {
        var expr = CronExpression.Parse(cronExpression);
        var tz = string.IsNullOrWhiteSpace(timeZoneId)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var next = expr.GetNextOccurrence(from, tz)
            ?? throw new InvalidOperationException(
                $"Cron expression '{cronExpression}' has no future occurrences after {from:O}");
        return next;
    }
}
