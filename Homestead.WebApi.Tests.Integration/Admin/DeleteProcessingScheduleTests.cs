using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Admin;

[Collection("Integration")]
public class DeleteProcessingScheduleTests : IntegrationTestBase
{
    public DeleteProcessingScheduleTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Delete_Existing_Returns204_RemovesRow()
    {
        var scheduleId = await SeedScheduleAsync("To delete", jobType: ProcessingJobType.NoOp);

        var client = await CreateAdminClientAsync();
        var response = await client.DeleteAsync($"/api/v1/admin/schedules/{scheduleId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.ProcessingSchedules.AsNoTracking()
            .AnyAsync(s => s.Id == scheduleId, CT);
        Assert.False(exists);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var response = await client.DeleteAsync($"/api/v1/admin/schedules/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AsNonAdmin_Returns403()
    {
        var scheduleId = await SeedScheduleAsync("Untouchable", jobType: ProcessingJobType.NoOp);

        var client = await CreateUserClientAsync();
        var response = await client.DeleteAsync($"/api/v1/admin/schedules/{scheduleId}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.ProcessingSchedules.AsNoTracking()
            .AnyAsync(s => s.Id == scheduleId, CT);
        Assert.True(exists);
    }

    [Fact]
    public async Task Delete_WithHistoricalJob_NullifiesScheduleIdOnJob()
    {
        var scheduleId = await SeedScheduleAsync("With history", jobType: ProcessingJobType.NoOp);

        Guid jobId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.NoOp,
                Status = ProcessingJobStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                ScheduleId = scheduleId,
            };
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync(CT);
            jobId = job.Id;
        }

        var client = await CreateAdminClientAsync();
        var response = await client.DeleteAsync($"/api/v1/admin/schedules/{scheduleId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ProcessingJobs.AsNoTracking()
                .FirstAsync(j => j.Id == jobId, CT);
            Assert.Null(job.ScheduleId);
        }
    }
}
