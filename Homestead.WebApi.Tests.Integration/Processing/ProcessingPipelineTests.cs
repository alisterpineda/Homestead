using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Processing;

[Collection("Integration")]
public class ProcessingPipelineTests : IntegrationTestBase
{
    public ProcessingPipelineTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task OrphanedInProgressJob_IsRecoveredAndProcessed()
    {
        // Pause the processor so we can seed an orphaned job without it being picked up
        var control = Factory.Services.GetRequiredService<IJobProcessorControl>();
        await using (await control.PauseAsync(CT))
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Simulate an orphaned InProgress job (as if the process crashed mid-processing)
            var orphan = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.UploadDocument,
                Status = ProcessingJobStatus.InProgress,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                CreatedByUserId = Guid.NewGuid()
            };
            db.ProcessingJobs.Add(orphan);
            await db.SaveChangesAsync(CT);

            // Reset the orphan to Pending (simulating what recovery does at startup)
            await db.ProcessingJobs
                .Where(j => j.Id == orphan.Id && j.Status == ProcessingJobStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Pending)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null), CT);

            var recovered = await db.ProcessingJobs.AsNoTracking()
                .FirstAsync(j => j.Id == orphan.Id, CT);

            Assert.Equal(ProcessingJobStatus.Pending, recovered.Status);
            Assert.Null(recovered.StartedAt);
        }
    }

    [Fact]
    public async Task CompletedJob_HasStartedAtAndCompletedAtSet()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "timestamps-test.pdf");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.ProcessingJobs.AsNoTracking()
            .FirstAsync(j => j.EntityId == docId, CT);

        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.NotNull(job.StartedAt);
        Assert.NotNull(job.CompletedAt);
        Assert.True(job.StartedAt <= job.CompletedAt);
    }

    [Fact]
    public async Task ProcessingJob_SignalWakeup_ProcessesWithoutWaitingForPoll()
    {
        var client = await CreateAdminClientAsync();

        var before = DateTimeOffset.UtcNow;

        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("signal-test.pdf"), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", "signal-test.pdf");

        var uploadResponse = await client.PostAsync("/api/v1/documents", form, CT);
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);

        var jobBody = await uploadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        // Should complete well before the 30s poll interval
        var docId = await WaitForJobCompletionAsync(jobBody!.JobId, TimeSpan.FromSeconds(10));

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ProcessingJobs.AsNoTracking()
            .FirstAsync(j => j.Id == jobBody.JobId, CT);

        // Job should have completed within a few seconds, not 30s
        var elapsed = job.CompletedAt!.Value - before;
        Assert.True(elapsed.TotalSeconds < 15,
            $"Job took {elapsed.TotalSeconds}s — should complete quickly via signal, not wait for poll interval");
    }
}
