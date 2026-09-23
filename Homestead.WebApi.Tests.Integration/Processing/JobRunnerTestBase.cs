using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Processing;

/// <summary>
/// Shared seams for driving <see cref="JobRunner"/> directly: tests hold <see cref="IJobProcessorControl.PauseAsync"/>
/// so the background loop cannot race them, then call <see cref="JobRunner.RunOnceAsync"/> and assert on
/// the row. Anything time-dependent — a due retry, an expired lease — is written into the row rather than
/// waited for.
/// </summary>
public abstract class JobRunnerTestBase : IntegrationTestBase
{
    protected JobRunnerTestBase(PostgresFixture fixture) : base(fixture)
    {
        Gate.Reset();
    }

    protected JobRunner Runner => Factory.Services.GetRequiredService<JobRunner>();
    protected TestJobGate Gate => Factory.Services.GetRequiredService<TestJobGate>();
    protected JobHookProbe Probe => Factory.Services.GetRequiredService<JobHookProbe>();
    protected NotificationProbe Notifications => Factory.Services.GetRequiredService<NotificationProbe>();

    protected Task<IAsyncDisposable> PauseAsync() =>
        Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);

    protected void Signal() => Factory.Services.GetRequiredService<IJobSignal>().Signal();

    /// <summary>Inserts a job, letting EF mint its key, and returns the key.</summary>
    protected async Task<Guid> SeedAsync(ProcessingJob job)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(CT);
        return job.Id;
    }

    protected static ProcessingJob NoOp(
        TestNoOpPayload? payload = null,
        ProcessingJobStatus status = ProcessingJobStatus.Pending,
        Guid? parentJobId = null,
        int priority = 0)
    {
        var job = new ProcessingJob
        {
            Type = ProcessingJobType.NoOp,
            Status = status,
            ParentJobId = parentJobId,
            Priority = priority,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = status == ProcessingJobStatus.InProgress ? DateTimeOffset.UtcNow : null,
        };

        if (payload is not null)
            job.SetPayload(payload);

        return job;
    }

    protected async Task<ProcessingJob> ReadAsync(Guid jobId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, CT);
    }

    protected async Task UpdateAsync(
        Guid jobId, Action<UpdateSettersBuilder<ProcessingJob>> set)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ProcessingJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(set, CT);
    }

    /// <summary>Brings a scheduled retry due now rather than waiting out its backoff.</summary>
    protected Task MakeDueAsync(Guid jobId) =>
        UpdateAsync(jobId, s => s.SetProperty(j => j.RunAfter, DateTimeOffset.UtcNow.AddSeconds(-1)));
}
