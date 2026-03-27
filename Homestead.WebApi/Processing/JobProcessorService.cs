using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Processing;

public class JobProcessorService(
    IServiceScopeFactory scopeFactory,
    IOptions<JobProcessingOptions> options,
    ILogger<JobProcessorService> logger) : BackgroundService, IJobProcessorControl, IJobSignal
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(5);
    private readonly int _maxConcurrency = Math.Max(1, options.Value.MaxConcurrency);
    private readonly SemaphoreSlim _processingGate = new(
        Math.Max(1, options.Value.MaxConcurrency),
        Math.Max(1, options.Value.MaxConcurrency));
    private readonly SemaphoreSlim _signal = new(0);

    public void Signal()
    {
        _signal.Release();
    }

    public async Task<IAsyncDisposable> PauseAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < _maxConcurrency; i++)
            await _processingGate.WaitAsync(ct);
        return new PauseHandle(_processingGate, _maxConcurrency);
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
        while (_signal.Wait(TimeSpan.Zero, CancellationToken.None))
        {
        }
    }

    private async Task DrainPendingJobsAsync(CancellationToken ct)
    {
        var activeTasks = new List<Task>();

        try
        {
            while (true)
            {
                await _processingGate.WaitAsync(ct);

                Guid? jobId;
                try
                {
                    jobId = await TryClaimNextJobAsync(ct);
                }
                catch
                {
                    _processingGate.Release();
                    throw;
                }

                if (jobId is null)
                {
                    _processingGate.Release();
                    break;
                }

                var capturedJobId = jobId.Value;
                // CancellationToken.None ensures the task is always scheduled so the
                // finally block releases the semaphore even during shutdown.
                activeTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(capturedJobId, ct);
                    }
                    finally
                    {
                        _processingGate.Release();
                    }
                }, CancellationToken.None));
            }
        }
        finally
        {
            // Await all dispatched jobs even if the drain loop was interrupted
            await Task.WhenAll(activeTasks);
        }
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Skip parent jobs that already spawned children — they are legitimately
            // InProgress (waiting for children to complete), not orphaned.
            var recovered = await db.ProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.InProgress)
                .Where(j => j.TotalChildCount == 0)
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
    /// Finds the highest-priority pending job and atomically claims it.
    /// Returns the claimed job ID, or null if the queue is empty.
    /// </summary>
    private async Task<Guid?> TryClaimNextJobAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        while (true)
        {
            var nextJobId = await db.ProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Pending)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .Select(j => (Guid?)j.Id)
                .FirstOrDefaultAsync(ct);

            if (nextJobId is null)
                return null;

            var claimed = await db.ProcessingJobs
                .Where(j => j.Id == nextJobId.Value && j.Status == ProcessingJobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.InProgress)
                    .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow), ct);

            if (claimed > 0)
                return nextJobId.Value;

            // Job was claimed by another path; try the next one
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
                return;
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

            if (handler is IParentJobHandler)
            {
                // Parent stays InProgress until UpdateParentProgressAsync transitions it
                // when the last child completes.
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Parent job {JobId} ({JobType}) spawned {ChildCount} children",
                    job.Id, job.Type, job.TotalChildCount);
            }
            else
            {
                job.Status = ProcessingJobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                if (job.ParentJobId is not null)
                    await UpdateParentProgressAsync(db, job.ParentJobId.Value, scope.ServiceProvider, ct);

                logger.LogInformation("Job {JobId} ({JobType}) completed successfully", job.Id, job.Type);
            }
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

                    if (failedJob.ParentJobId is not null)
                        await UpdateParentProgressAsync(errorDb, failedJob.ParentJobId.Value, errorScope.ServiceProvider, ct);
                }
            }
            catch (Exception innerEx)
            {
                logger.LogError(innerEx, "Failed to mark job {JobId} as failed", jobId);
            }
        }
    }

    private static async Task UpdateParentProgressAsync(
        AppDbContext db, Guid parentJobId, IServiceProvider sp, CancellationToken ct)
    {
        var stats = await db.ProcessingJobs
            .Where(j => j.ParentJobId == parentJobId)
            .GroupBy(j => 1)
            .Select(g => new
            {
                Completed = g.Count(j => j.Status == ProcessingJobStatus.Completed),
                Failed = g.Count(j => j.Status == ProcessingJobStatus.Failed),
            })
            .FirstOrDefaultAsync(ct);

        if (stats is null) return;

        // Read parent fields needed for notification and terminal check
        var parent = await db.ProcessingJobs
            .AsNoTracking()
            .Where(j => j.Id == parentJobId)
            .Select(j => new
            {
                j.TotalChildCount,
                j.EntityType,
                j.EntityId,
                j.CreatedByUserId,
            })
            .FirstAsync(ct);

        var notifier = sp.GetRequiredService<IProcessingNotifier>();
        var finishedCount = stats.Completed + stats.Failed;

        ProcessingJobStatus? notifyStatus = null;

        if (finishedCount >= parent.TotalChildCount)
        {
            var terminalStatus = stats.Failed == 0
                ? ProcessingJobStatus.Completed
                : stats.Completed == 0
                    ? ProcessingJobStatus.Failed
                    : ProcessingJobStatus.PartiallyCompleted;

            // Atomically transition parent to terminal status — only one concurrent caller wins.
            // Uses the same WHERE-guarded ExecuteUpdateAsync pattern as job claiming.
            var transitioned = await db.ProcessingJobs
                .Where(j => j.Id == parentJobId && j.Status == ProcessingJobStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, terminalStatus)
                    .SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow)
                    .SetProperty(j => j.CompletedChildCount, stats.Completed)
                    .SetProperty(j => j.FailedChildCount, stats.Failed), ct);

            if (transitioned > 0)
                notifyStatus = terminalStatus;
        }
        else
        {
            // Update progress counters only if parent is still InProgress (prevents
            // overwriting final counts set by the terminal transition).
            var updated = await db.ProcessingJobs
                .Where(j => j.Id == parentJobId && j.Status == ProcessingJobStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.CompletedChildCount, stats.Completed)
                    .SetProperty(j => j.FailedChildCount, stats.Failed), ct);

            if (updated > 0)
                notifyStatus = ProcessingJobStatus.InProgress;
        }

        if (notifyStatus is not null)
        {
            await notifier.NotifyJobStatusAsync(
                parentJobId, notifyStatus.Value,
                parent.EntityType, parent.EntityId,
                documentName: null, error: null,
                parent.CreatedByUserId, ct,
                totalChildCount: parent.TotalChildCount,
                completedChildCount: stats.Completed,
                failedChildCount: stats.Failed);
        }
    }

    private sealed class PauseHandle(SemaphoreSlim semaphore, int count) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                semaphore.Release(count);
            return ValueTask.CompletedTask;
        }
    }
}
