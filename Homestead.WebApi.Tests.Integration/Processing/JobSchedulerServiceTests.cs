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

        var dueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var scheduleId = await SeedScheduleAsync(nextRunAt: dueAt, cron: "*/5 * * * *");

        var result = await Scheduler.TickOnceAsync(CT);

        Assert.Equal(ScheduleTickResult.Fired, result);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.NotNull(schedule.LastRunAt);
        Assert.True(schedule.NextRunAt > DateTimeOffset.UtcNow,
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

        // Schedule was due 2 days ago (cron is every 5 min ⇒ hundreds of missed ticks).
        var scheduleId = await SeedScheduleAsync(
            nextRunAt: DateTimeOffset.UtcNow.AddDays(-2),
            cron: "*/5 * * * *");

        var first = await Scheduler.TickOnceAsync(CT);
        Assert.Equal(ScheduleTickResult.Fired, first);

        // After firing once, NextRunAt should be in the future — a second invocation must not fire.
        var second = await Scheduler.TickOnceAsync(CT);
        Assert.Equal(ScheduleTickResult.NothingDue, second);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == scheduleId, CT);
        Assert.True(schedule.NextRunAt > DateTimeOffset.UtcNow);

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
