using System.Threading.Channels;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Processing;

public class JobProcessorService(
    Channel<Guid> channel,
    IServiceScopeFactory scopeFactory,
    ILogger<JobProcessorService> logger) : BackgroundService, IJobProcessorControl
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _processingGate = new(1, 1);

    public async Task<IAsyncDisposable> PauseAsync(CancellationToken ct = default)
    {
        await _processingGate.WaitAsync(ct);
        return new PauseHandle(_processingGate);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channelTask = ProcessChannelAsync(stoppingToken);
        var pollingTask = PollForJobsAsync(stoppingToken);

        await Task.WhenAll(channelTask, pollingTask);
    }

    private async Task ProcessChannelAsync(CancellationToken ct)
    {
        await foreach (var jobId in channel.Reader.ReadAllAsync(ct))
        {
            await TryProcessJobAsync(jobId, ct);
        }
    }

    private async Task PollForJobsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);
                await ClaimAndProcessPendingJobsAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during job polling");
            }
        }
    }

    private async Task ClaimAndProcessPendingJobsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingJobIds = await db.ProcessingJobs
            .Where(j => j.Status == ProcessingJobStatus.Pending)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .ToListAsync(ct);

        foreach (var jobId in pendingJobIds)
        {
            await TryProcessJobAsync(jobId, ct);
        }
    }

    private async Task TryProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        await _processingGate.WaitAsync(ct);
        try
        {
            await ProcessJobCoreAsync(jobId, ct);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    private async Task ProcessJobCoreAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IJobHandler>>();

            // Atomic claim: only update if still Pending
            var claimed = await db.ProcessingJobs
                .Where(j => j.Id == jobId && j.Status == ProcessingJobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.InProgress)
                    .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow), ct);

            if (claimed == 0)
                return; // Already claimed by channel or poller

            var job = await db.ProcessingJobs.FirstAsync(j => j.Id == jobId, ct);

            var handler = handlers.FirstOrDefault(h => h.Type == job.Type);
            if (handler is null)
            {
                logger.LogError("No handler registered for job type {JobType}", job.Type);
                job.Status = ProcessingJobStatus.Failed;
                job.Error = $"No handler registered for job type '{job.Type}'";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            await handler.HandleAsync(job, ct);

            job.Status = ProcessingJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Job {JobId} ({JobType}) completed successfully", job.Id, job.Type);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed", jobId);

            try
            {
                await using var errorScope = scopeFactory.CreateAsyncScope();
                var errorDb = errorScope.ServiceProvider.GetRequiredService<AppDbContext>();

                var failedJob = await errorDb.ProcessingJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
                if (failedJob is not null)
                {
                    failedJob.Status = ProcessingJobStatus.Failed;
                    failedJob.Error = ex.Message;
                    failedJob.CompletedAt = DateTimeOffset.UtcNow;
                    await errorDb.SaveChangesAsync(ct);

                    string? errorCode = null;
                    Dictionary<string, string>? errorData = null;
                    if (ex is IStructuredJobError structured)
                    {
                        errorCode = structured.ErrorCode;
                        errorData = structured.ErrorData;
                    }

                    var notifier = errorScope.ServiceProvider.GetRequiredService<IProcessingNotifier>();
                    await notifier.NotifyJobStatusAsync(
                        failedJob.Id, ProcessingJobStatus.Failed,
                        failedJob.EntityType, failedJob.EntityId,
                        documentName: null, error: ex.Message,
                        failedJob.CreatedByUserId, ct,
                        errorCode: errorCode, errorData: errorData);
                }
            }
            catch (Exception innerEx)
            {
                logger.LogError(innerEx, "Failed to mark job {JobId} as failed", jobId);
            }
        }
    }

    private sealed class PauseHandle(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
