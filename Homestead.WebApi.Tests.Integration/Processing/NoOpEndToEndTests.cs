using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Processing;

[Collection("Integration")]
public class NoOpEndToEndTests : IntegrationTestBase
{
    public NoOpEndToEndTests(PostgresFixture fixture) : base(fixture) { }

    private IJobSchedulerControl Scheduler =>
        Factory.Services.GetRequiredService<IJobSchedulerControl>();

    [Fact]
    public async Task NoOpSchedule_FiresAndCompletesEndToEnd()
    {
        var scheduleId = await SeedScheduleAsync(
            "Smoke NoOp",
            jobType: ProcessingJobType.NoOp,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cron: "*/5 * * * *");

        var tickResult = await Scheduler.TickOnceAsync(CT);
        Assert.Equal(ScheduleTickResult.Fired, tickResult);

        Guid jobId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var schedule = await db.ProcessingSchedules.AsNoTracking()
                .FirstAsync(s => s.Id == scheduleId, CT);
            Assert.NotNull(schedule.LastJobId);
            jobId = schedule.LastJobId.Value;
        }

        await WaitForJobCompletionAsync(jobId);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, CT);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);
            Assert.Equal(ProcessingJobType.NoOp, job.Type);
            Assert.Equal(scheduleId, job.ScheduleId);
            Assert.Null(job.Error);
        }
    }
}
