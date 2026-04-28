using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Processing;

[Collection("Integration")]
public class NoOpAdminSmokeTestFlowTests : IntegrationTestBase
{
    public NoOpAdminSmokeTestFlowTests(PostgresFixture fixture) : base(fixture) { }

    private IJobSchedulerControl Scheduler =>
        Factory.Services.GetRequiredService<IJobSchedulerControl>();

    [Fact]
    public async Task AdminCreatesNoOpSchedule_TicksToCompletion_ThenDeletes()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Smoke flow",
            jobType = ProcessingJobType.NoOp.ToString(),
            cronExpression = "*/5 * * * *",
            priority = 0,
            isEnabled = true,
        }, CT);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateProcessingScheduleResponse>(CT);
        Assert.NotNull(created);
        var scheduleId = created.Id;

        // Bump NextRunAt into the past so the next tick fires deterministically rather than
        // waiting on wall-clock for the cron's next future occurrence.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ProcessingSchedules
                .Where(s => s.Id == scheduleId)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(s => s.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1)), CT);
        }

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

        var deleteResponse = await client.DeleteAsync($"/api/v1/admin/schedules/{scheduleId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stillExists = await db.ProcessingSchedules.AsNoTracking()
                .AnyAsync(s => s.Id == scheduleId, CT);
            Assert.False(stillExists);

            var job = await db.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, CT);
            Assert.Null(job.ScheduleId);
        }
    }
}
