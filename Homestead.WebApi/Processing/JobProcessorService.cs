using Homestead.WebApi.Common;
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
    private readonly TimeSpan _jobTimeout = options.Value.ResolveJobTimeout();
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
        // The build-time OpenAPI generator boots the host with no database; skip all polling so spec
        // generation stays quiet and side-effect free.
        if (BuildTimeOpenApi.IsGenerating)
            return;

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
                // Pruned every pass, not only when the claim comes back empty: a long drain dispatches one
                // task per job, and a list that only ever grows would make the WhenAny below — and the
                // WhenAll in the finally — walk every job the drain has ever started.
                activeTasks.RemoveAll(t => t.IsCompleted);

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

                    // Null no longer means "the queue is empty" — it is also the normal state at a stage
                    // boundary, where every remaining sibling is blocked behind one that is still running.
                    // Exiting here would make the `finally` await EVERY dispatched task before the loop
                    // could re-drain, so an upload arriving mid-run would wait out the longest in-flight
                    // backup batch instead of preempting at the next freed permit. Wait for the FIRST task
                    // instead and re-try the claim.
                    //
                    // Nothing can be stranded by this: if nothing is in flight and a job is still blocked,
                    // its blocker is Pending, and following that chain upward always bottoms out at a
                    // claimable job. WhenAny is safe because dispatched tasks never throw — ProcessJobAsync
                    // catches everything.
                    if (activeTasks.Count == 0)
                        break;

                    await Task.WhenAny(activeTasks);
                    continue;
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
    /// Finds the highest-priority claimable pending job and atomically claims it.
    /// Returns the claimed job ID, or null if nothing is currently claimable.
    /// </summary>
    /// <remarks>
    /// Among ONE parent's children, a strictly higher <c>Priority</c> means <b>runs before</b>, not merely
    /// "claimed sooner" — a happens-before edge the queue enforces. Equal priority imposes no edge, so an
    /// ordinary flat fan-out (every child at the default 0) is unaffected and still runs fully concurrent.
    /// </remarks>
    private async Task<Guid?> TryClaimNextJobAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        while (true)
        {
            var nextJobId = await db.ProcessingJobs
                .Where(j => j.Status == ProcessingJobStatus.Pending)
                // Blocked-sibling predicate: a child waits while any sibling above it has yet to terminate.
                //
                // The `j.ParentJobId == null` guard is LOAD-BEARING and must not be simplified away. EF
                // Core's default C# null-semantics compensation expands `s.ParentJobId == j.ParentJobId`
                // with an `IS NULL AND IS NULL` arm, so without the guard every ROOT job would land in one
                // giant sibling group and a single high-priority root job would serialize the whole queue.
                .Where(j => j.ParentJobId == null || !db.ProcessingJobs.Any(s =>
                    s.ParentJobId == j.ParentJobId &&
                    s.Priority > j.Priority &&
                    (s.Status == ProcessingJobStatus.Pending || s.Status == ProcessingJobStatus.InProgress)))
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .Select(j => (Guid?)j.Id)
                .FirstOrDefaultAsync(ct);

            if (nextJobId is null)
                return null;

            // No need to re-assert the blocked-sibling predicate inside the UPDATE: it is monotone in the
            // safe direction, because a blocked job can only ever become unblocked. The one path that
            // restores a job to Pending, RecoverOrphanedJobsAsync, runs once before this loop ever starts.
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
                // Known gap, left alone: this fails the job without touching its parent at all — no
                // progress update and no dependent-sibling cancellation. Unreachable in practice, since a
                // job type with no registered handler cannot have been enqueued by this build.
                logger.LogError("No handler registered for job type {JobType}", job.Type);
                job.Status = ProcessingJobStatus.Failed;
                job.Error = $"No handler registered for job type '{job.Type}'";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_jobTimeout);

            try
            {
                await handler.HandleAsync(job, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Job {jobId} timed out after {_jobTimeout.TotalMinutes} minutes");
            }

            if (handler is IParentJobHandler)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Parent job {JobId} ({JobType}) spawned {ChildCount} children",
                    job.Id, job.Type, job.TotalChildCount);

                // A parent that spawned zero children has nothing to trigger finalization later,
                // so terminate it now (Completed) — see overview.md.
                if (job.TotalChildCount == 0)
                    await UpdateParentProgressAsync(db, job.Id, scope.ServiceProvider, ct);
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
                    var cancelledSiblings = await FailAndCancelDependentsAsync(errorDb, failedJob, ex);

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
                    {
                        if (cancelledSiblings > 0)
                            logger.LogWarning(
                                "Job {JobId} failed and cancelled {Count} dependent sibling(s) at lower priority",
                                failedJob.Id, cancelledSiblings);

                        await UpdateParentProgressAsync(errorDb, failedJob.ParentJobId.Value, errorScope.ServiceProvider, ct);
                    }
                }
            }
            catch (Exception innerEx)
            {
                logger.LogError(innerEx, "Failed to mark job {JobId} as failed", jobId);
            }
        }
    }

    /// <summary>
    /// Marks the job Failed and, in the same transaction, cancels the pending siblings that depended on
    /// it — the ones at strictly lower priority under the same parent. Returns how many were cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transaction is load-bearing, not decoration. Until it commits, other connections still see the
    /// failed job as <c>InProgress</c>, so the claim predicate keeps its lower-priority siblings
    /// unclaimable; after it commits they are <c>Cancelled</c>. There is no window in which a dependent
    /// sibling can be claimed — precisely the failure-then-crash window that would otherwise let a Put run
    /// after the Move it depended on had failed, aliasing two state rows onto one path.
    /// </para>
    /// <para>
    /// If the transaction never commits, the child stays <c>InProgress</c>, the startup sweep re-pends it
    /// (children carry <c>TotalChildCount == 0</c>) and it re-runs idempotently.
    /// </para>
    /// <para>
    /// <c>TotalChildCount</c> is DECREMENTED by the cancelled count rather than the tally being taught
    /// about Cancelled: it already means "children still expected to terminate", which is exactly what
    /// <c>EnqueueContinuationAsync</c> assumes when it increments. That keeps one code path for parent
    /// finalization — a cancelled child would otherwise leave <c>finishedCount &lt; TotalChildCount</c>
    /// forever and the parent would never finalize.
    /// </para>
    /// <para>
    /// An explicit <c>BeginTransactionAsync</c> is legal here because a job scope sits outside the command
    /// pipeline: the transaction interceptors are scope-gated off and EF execution-strategy retries are
    /// disabled. <c>CancellationToken.None</c> throughout — once the failure bookkeeping starts it must
    /// finish, and the caller's token may already be cancelled by shutdown.
    /// </para>
    /// </remarks>
    private static async Task<int> FailAndCancelDependentsAsync(
        AppDbContext db, ProcessingJob failedJob, Exception ex)
    {
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);

        failedJob.Status = ProcessingJobStatus.Failed;
        failedJob.Error = ex.Message;
        failedJob.CompletedAt = now;
        await db.SaveChangesAsync(CancellationToken.None);

        var cancelled = 0;

        if (failedJob.ParentJobId is { } parentId)
        {
            var failedPriority = failedJob.Priority;

            // Equal-priority siblings are untouched, so an ordinary flat fan-out (bulk classify, bulk
            // reprocess — every child at 0) is byte-identical to before and PartiallyCompleted stays a
            // first-class outcome.
            //
            // Error is stamped alongside the status: a bare Cancelled row is indistinguishable from one
            // torn down by the delete flow, and the admin looking at a red run needs to see that this
            // batch never ran BECAUSE of the one above it, not on its own account.
            var reason = $"Cancelled: a higher-priority sibling job failed ({failedJob.Id}).";

            cancelled = await db.ProcessingJobs
                .Where(j => j.ParentJobId == parentId &&
                            j.Status == ProcessingJobStatus.Pending &&
                            j.Priority < failedPriority)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Cancelled)
                    .SetProperty(j => j.Error, reason)
                    .SetProperty(j => j.CompletedAt, now), CancellationToken.None);

            if (cancelled > 0)
                await db.ProcessingJobs
                    .Where(j => j.Id == parentId && j.Status == ProcessingJobStatus.InProgress)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.TotalChildCount, j => j.TotalChildCount - cancelled),
                        CancellationToken.None);
        }

        await tx.CommitAsync(CancellationToken.None);

        return cancelled;
    }

    // Counts Completed and Failed children ONLY, and both writes below are guarded on the parent still
    // being InProgress. Do not "fix" the tally to include Cancelled — there are two producers of a
    // Cancelled child and neither wants one:
    //   • The backup source delete flow flips the parent and every Pending child in ONE transaction, so a
    //     still-InProgress parent never has Cancelled children. What CAN happen is the reverse — an
    //     already-InProgress child completes after its parent was cancelled — and the InProgress guards
    //     make that a natural no-op, leaving the Cancelled parent alone and sending no notification.
    //     Counting Cancelled children would instead let that straggler drive the cancelled parent to a
    //     terminal Completed/Failed.
    //   • FailAndCancelDependentsAsync cancels a failed child's lower-priority siblings, and in the SAME
    //     transaction decrements the parent's TotalChildCount by exactly that many. The expectation drops
    //     instead of the tally growing, so this method needs no knowledge of it: finishedCount still
    //     reaches TotalChildCount and the parent finalizes, normally as Failed or PartiallyCompleted.
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

        // Zero children => no child rows => grouped query returns null. Treat as 0/0 so the
        // terminal check below (0 finished >= 0 total, failed == 0) finalizes as Completed.
        var completed = stats?.Completed ?? 0;
        var failed = stats?.Failed ?? 0;

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
        var finishedCount = completed + failed;

        ProcessingJobStatus? notifyStatus = null;

        if (finishedCount >= parent.TotalChildCount)
        {
            var terminalStatus = failed == 0
                ? ProcessingJobStatus.Completed
                : completed == 0
                    ? ProcessingJobStatus.Failed
                    : ProcessingJobStatus.PartiallyCompleted;

            // Atomically transition parent to terminal status — only one concurrent caller wins.
            // Uses the same WHERE-guarded ExecuteUpdateAsync pattern as job claiming.
            var transitioned = await db.ProcessingJobs
                .Where(j => j.Id == parentJobId && j.Status == ProcessingJobStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, terminalStatus)
                    .SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow)
                    .SetProperty(j => j.CompletedChildCount, completed)
                    .SetProperty(j => j.FailedChildCount, failed), ct);

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
                    .SetProperty(j => j.CompletedChildCount, completed)
                    .SetProperty(j => j.FailedChildCount, failed), ct);

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
                completedChildCount: completed,
                failedChildCount: failed);
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
