using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Processing;

public class JobProcessorService(
    IServiceScopeFactory scopeFactory,
    ILogger<JobProcessorService> logger) : BackgroundService, IJobProcessorControl, IJobSignal
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0);

    public void Signal()
    {
        _signal.Release();
    }

    public async Task<IAsyncDisposable> PauseAsync(CancellationToken ct = default)
    {
        await _processingGate.WaitAsync(ct);
        return new PauseHandle(_processingGate);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverOrphanedJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WaitForSignalOrTimeoutAsync(stoppingToken);
                await DrainPendingJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in job processing loop");
            }
        }
    }

    private async Task WaitForSignalOrTimeoutAsync(CancellationToken ct)
    {
        await _signal.WaitAsync(PollInterval, ct);

        // Drain extra accumulated signals to prevent redundant empty DB queries
        while (_signal.Wait(TimeSpan.Zero))
        {
        }
    }

    private async Task DrainPendingJobsAsync(CancellationToken ct)
    {
        while (await TryProcessNextJobAsync(ct))
        {
        }
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var recovered = await db.ProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Pending)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null), ct);

            if (recovered > 0)
                logger.LogWarning("Recovered {Count} orphaned jobs at startup", recovered);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recover orphaned jobs at startup");
        }
    }

    /// <summary>
    /// Finds the highest-priority pending job, claims it, and processes it.
    /// Returns true if a job was found (regardless of success), false if the queue is empty.
    /// </summary>
    private async Task<bool> TryProcessNextJobAsync(CancellationToken ct)
    {
        await _processingGate.WaitAsync(ct);
        try
        {
            return await ProcessNextJobCoreAsync(ct);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    private async Task<bool> ProcessNextJobCoreAsync(CancellationToken ct)
    {
        Guid jobId = default;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Find and atomically claim the highest-priority pending job
            var nextJobId = await db.ProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Pending)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .Select(j => (Guid?)j.Id)
                .FirstOrDefaultAsync(ct);

            if (nextJobId is null)
                return false;

            jobId = nextJobId.Value;

            var claimed = await db.ProcessingJobs
                .Where(j => j.Id == jobId && j.Status == ProcessingJobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.InProgress)
                    .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow), ct);

            if (claimed == 0)
                return true; // Job was claimed by another path; keep draining

            var job = await db.ProcessingJobs.FirstAsync(j => j.Id == jobId, ct);
            var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IJobHandler>>();

            var handler = handlers.FirstOrDefault(h => h.Type == job.Type);
            if (handler is null)
            {
                logger.LogError("No handler registered for job type {JobType}", job.Type);
                job.Status = ProcessingJobStatus.Failed;
                job.Error = $"No handler registered for job type '{job.Type}'";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return true;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(JobTimeout);

            try
            {
                await handler.HandleAsync(job, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Job {jobId} timed out after {JobTimeout.TotalMinutes} minutes");
            }

            job.Status = ProcessingJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Job {JobId} ({JobType}) completed successfully", job.Id, job.Type);
            return true;
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

            return true;
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
