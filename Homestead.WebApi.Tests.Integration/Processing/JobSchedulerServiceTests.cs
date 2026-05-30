using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Processing;

[Collection("Integration")]
public class JobSchedulerServiceTests : IntegrationTestBase
{
    public JobSchedulerServiceTests(PostgresFixture fixture) : base(fixture) { }

    private IJobSchedulerControl Scheduler =>
        Factory.Services.GetRequiredService<IJobSchedulerControl>();

    private Task<IAsyncDisposable> PauseWorkerAsync() =>
        Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);

    [Fact]
    public async Task TickOnce_DueSchedule_FiresAndAdvances()
    {
        await using var _ = await PauseWorkerAsync();

        // Pin the clock so the post-fire "NextRunAt is in the future" assertion compares against the same
        // instant the scheduler advanced from, not a later real-clock read that could straddle a cron
        // boundary and see the freshly-advanced NextRunAt as already stale.
        using var _clock = Factory.Clock.Freeze();
        var now = Factory.Clock.GetUtcNow();

        var scheduleId = await SeedScheduleAsync(nextRunAt: now.AddMinutes(-1), cron: "*/5 * * * *");

        var result = await Scheduler.TickOnceAsync(CT);

        Assert.Equal(ScheduleTickResult.Fired, result);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.NotNull(schedule.LastRunAt);
        Assert.True(schedule.NextRunAt > now,
            $"NextRunAt should advance past now; got {schedule.NextRunAt:O}");
        Assert.NotNull(schedule.LastJobId);

        var job = await db.ProcessingJobs.AsNoTracking()
            .FirstAsync(j => j.Id == schedule.LastJobId, CT);
        Assert.Equal(scheduleId, job.ScheduleId);
        Assert.Equal(ProcessingJobType.UploadDocument, job.Type);
    }

    [Fact]
    public async Task TickOnce_DisabledSchedule_DoesNotFire()
    {
        await using var _ = await PauseWorkerAsync();

        var scheduleId = await SeedScheduleAsync(
            isEnabled: false,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cron: "*/5 * * * *");

        var result = await Scheduler.TickOnceAsync(CT);

        Assert.Equal(ScheduleTickResult.NothingDue, result);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.Null(schedule.LastRunAt);
        Assert.Null(schedule.LastJobId);
    }

    [Fact]
    public async Task TickOnce_NotYetDueSchedule_DoesNotFire()
    {
        await using var _ = await PauseWorkerAsync();

        var scheduleId = await SeedScheduleAsync(
            nextRunAt: DateTimeOffset.UtcNow.AddHours(1),
            cron: "*/5 * * * *");

        var result = await Scheduler.TickOnceAsync(CT);

        Assert.Equal(ScheduleTickResult.NothingDue, result);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.Null(schedule.LastRunAt);
    }

    [Fact]
    public async Task TickOnce_ConcurrentClaims_OnlyOneFires()
    {
        await using var _ = await PauseWorkerAsync();

        var scheduleId = await SeedScheduleAsync(
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cron: "*/5 * * * *");

        var results = await Task.WhenAll(
            Scheduler.TickOnceAsync(CT),
            Scheduler.TickOnceAsync(CT));

        Assert.Single(results, r => r == ScheduleTickResult.Fired);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobCount = await db.ProcessingJobs.CountAsync(j => j.ScheduleId == scheduleId, CT);
        Assert.Equal(1, jobCount);
    }

    [Fact]
    public async Task TickOnce_MissedTicks_FiresOnce_AdvancesPastNow()
    {
        await using var _ = await PauseWorkerAsync();

        // Pin the clock so both ticks observe the same "now". The first tick advances NextRunAt to the
        // next cron occurrence strictly after now; with now frozen, the second tick reads that same now
        // and so cannot see the schedule as due again — regardless of how close a cron boundary sits to
        // the real wall clock. (Under the real clock the two reads could straddle a boundary and re-fire.)
        using var _clock = Factory.Clock.Freeze();
        var now = Factory.Clock.GetUtcNow();

        // Schedule was due 2 days ago — overdue when the first tick runs.
        var scheduleId = await SeedScheduleAsync(
            nextRunAt: now.AddDays(-2),
            cron: "*/5 * * * *");

        var first = await Scheduler.TickOnceAsync(CT);
        Assert.Equal(ScheduleTickResult.Fired, first);

        // After firing once, NextRunAt is in the future — a second invocation must not fire.
        var second = await Scheduler.TickOnceAsync(CT);
        Assert.Equal(ScheduleTickResult.NothingDue, second);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.True(schedule.NextRunAt > now);

        var jobCount = await db.ProcessingJobs.CountAsync(j => j.ScheduleId == scheduleId, CT);
        Assert.Equal(1, jobCount);
    }

    [Fact]
    public async Task TickOnce_InvalidCron_DisablesSchedule()
    {
        await using var _ = await PauseWorkerAsync();

        var scheduleId = await SeedScheduleAsync(
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cron: "not a cron");

        var result = await Scheduler.TickOnceAsync(CT);
        Assert.Equal(ScheduleTickResult.DisabledInvalidCron, result);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.False(schedule.IsEnabled);
        Assert.Null(schedule.LastJobId);
    }

    [Fact]
    public async Task TickOnce_NoSchedules_ReturnsNothingDue()
    {
        await using var _ = await PauseWorkerAsync();

        var result = await Scheduler.TickOnceAsync(CT);

        Assert.Equal(ScheduleTickResult.NothingDue, result);
    }
}
