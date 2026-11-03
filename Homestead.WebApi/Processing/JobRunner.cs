using System.Linq.Expressions;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Processing;

/// <summary>
/// Claims a job, holds its lease, runs its handler, and records the outcome — the whole of the queue's
/// per-job behaviour. <see cref="JobProcessorService"/> only decides <i>when</i> to call it.
/// </summary>
/// <remarks>
/// <para>
/// Split out so claim, lease, and retry semantics are testable by calling <see cref="RunOnceAsync"/>
/// directly against a real Postgres while the background loop is paused, rather than by polling for a
/// status the loop may or may not have reached yet.
/// </para>
/// <para>
/// <b>Clocks.</b> Every lease and <c>RunAfter</c> comparison, and every timestamp the processor writes
/// onto a job it holds, uses the database's <c>now()</c> — <c>DateTimeOffset.UtcNow</c> inside an
/// expression tree, which Npgsql translates. One clock decides what "expired" and "due" mean, whichever
/// process is asking, and the test host's controllable <see cref="TimeProvider"/> never leaks in.
/// In <c>ExecuteUpdateAsync</c> that means the <i>lambda</i> overload,
/// <c>SetProperty(j =&gt; j.X, j =&gt; DateTimeOffset.UtcNow)</c>: the value overload evaluates its argument
/// in C# and sends the app's clock as a parameter.
/// </para>
/// <para>
/// <b>Fencing.</b> <c>StartedAt</c> is re-stamped by every claim, so it doubles as the claim's identity:
/// the heartbeat and every terminal write match on <c>StartedAt == claimedAt</c>. A runner whose lease
/// expired and whose job was reclaimed therefore neither extends the new holder's lease nor overwrites
/// its outcome — it finds zero rows and stands down.
/// </para>
/// </remarks>
public sealed class JobRunner(
    IServiceScopeFactory scopeFactory,
    IOptions<JobProcessingOptions> options,
    ILogger<JobRunner> logger)
{
    /// <summary>How long a final-failure hook may run. Not linked to shutdown: it runs once, or never.</summary>
    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest budget a handler may declare through <see cref="IJobHandler.ResolveTimeout"/>.</summary>
    internal static readonly TimeSpan MaxDeclaredTimeout = TimeSpan.FromDays(1);

    /// <summary>Slack added to a computed wake-up so the claim does not race the instant it becomes due.</summary>
    private static readonly TimeSpan WakeSlack = TimeSpan.FromMilliseconds(50);

    private readonly TimeSpan _jobTimeout = options.Value.ResolveJobTimeout();
    private readonly TimeSpan _leaseDuration = options.Value.LeaseDuration;
    private readonly TimeSpan _heartbeatInterval = options.Value.HeartbeatInterval;
    private readonly TimeSpan _pollInterval = options.Value.PollInterval;
    private readonly IReadOnlyList<TimeSpan> _retryBackoff = options.Value.ResolveRetryBackoff();

    /// <summary>What the claim may take: a due Pending job, or a childless one whose runner has died.</summary>
    /// <remarks>
    /// <para>
    /// The second arm is crash recovery. A process that dies mid-job leaves its row <c>InProgress</c>
    /// with a lease nobody renews; once that lease expires the ordinary claim takes it back, so there is
    /// no startup sweep and nothing waits for a restart.
    /// </para>
    /// <para>
    /// <c>TotalChildCount == 0</c> is load-bearing. Parent handlers
    /// save their children and <c>TotalChildCount</c> in their own <c>SaveChanges</c>, before the
    /// processor gets to clear the lease. Reclaiming a parent past that point would re-run its handler,
    /// whose idempotency branch resets <c>TotalChildCount</c> to the number of child rows — undoing any
    /// decrement for children already cancelled, so the parent would never finalize. A parent with
    /// children is legitimately waiting on them, lease or no lease.
    /// </para>
    /// <para>
    /// A NULL lease on an <c>InProgress</c> row is never reclaimable: that is a parent waiting on its
    /// children, or a row written by hand.
    /// </para>
    /// <para>
    /// <see cref="GetIdleDelayAsync"/> mirrors both arms with the time comparison flipped, to find when the
    /// next row becomes claimable. A new or changed arm here must be mirrored there, or the idle loop
    /// silently falls back to the poll interval for it.
    /// </para>
    /// </remarks>
    private static readonly Expression<Func<ProcessingJob, bool>> IsClaimable = j =>
        (j.Status == ProcessingJobStatus.Pending &&
         (j.RunAfter == null || j.RunAfter <= DateTimeOffset.UtcNow)) ||
        (j.Status == ProcessingJobStatus.InProgress &&
         j.LockedUntil != null && j.LockedUntil < DateTimeOffset.UtcNow &&
         j.TotalChildCount == 0);

    /// <summary>
    /// The fence every write on a held job matches: the row is still the one this run claimed. See
    /// <b>Fencing</b> in the class remarks.
    /// </summary>
    private static Expression<Func<ProcessingJob, bool>> HeldBy(Guid jobId, DateTimeOffset claimedAt) => j =>
        j.Id == jobId &&
        j.Status == ProcessingJobStatus.InProgress &&
        j.StartedAt == claimedAt;

    private static IJobHandler? ResolveHandler(IServiceProvider sp, ProcessingJobType type) =>
        sp.GetRequiredService<IEnumerable<IJobHandler>>().FirstOrDefault(h => h.Type == type);

    /// <summary>The attempt budget actually enforced: at least one run, whatever the handler says.</summary>
    private static int EffectiveMaxAttempts(IJobHandler? handler) => Math.Max(1, handler?.MaxAttempts ?? 1);

    /// <summary>
    /// Whether a run of failed heartbeats has gone on long enough that the last good lease could have
    /// expired, so another runner may legitimately claim the job and carrying on would run it twice.
    /// </summary>
    internal static bool ShouldGiveUpLease(
        DateTimeOffset now, DateTimeOffset lastKnownExpiry, TimeSpan heartbeatInterval) =>
        now >= lastKnownExpiry - heartbeatInterval;

    /// <summary>Claims at most one due job and runs it to an outcome.</summary>
    /// <param name="ct">Host shutdown. A job interrupted by it is handed back to the queue, not failed.</param>
    /// <returns>True if a job was claimed.</returns>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var jobId = await TryClaimNextJobAsync(ct);
        if (jobId is null)
            return false;

        await RunClaimedJobAsync(jobId.Value, ct);
        return true;
    }

    /// <summary>
    /// Finds the highest-priority claimable job and atomically claims it, starting its lease.
    /// Returns the claimed job ID, or null if nothing is currently claimable.
    /// </summary>
    /// <remarks>
    /// Among ONE parent's children, a strictly higher <c>Priority</c> means <b>runs before</b>, not merely
    /// "claimed sooner" — a happens-before edge the queue enforces. Equal priority imposes no edge, so an
    /// ordinary flat fan-out (every child at the default 0) is unaffected and still runs fully concurrent.
    /// </remarks>
    public async Task<Guid?> TryClaimNextJobAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lease = _leaseDuration;

        while (true)
        {
            var nextJobId = await db.ProcessingJobs
                .Where(IsClaimable)
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

            // IsClaimable is re-asserted, which is what makes concurrent claimers safe: under READ
            // COMMITTED Postgres re-checks the WHERE against the newest row version after waiting on a
            // competing writer, so a claim or heartbeat that landed in between turns this into zero rows.
            //
            // The blocked-sibling predicate need not be: it is monotone in the safe direction. The blocking
            // set is {Pending, InProgress}; a retry or a shutdown release moves a job InProgress → Pending,
            // inside that set, and a terminal status never changes back. So a job that was unblocked when
            // selected cannot have become blocked since.
            var claimed = await db.ProcessingJobs
                .Where(j => j.Id == nextJobId.Value)
                .Where(IsClaimable)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.InProgress)
                    .SetProperty(j => j.StartedAt, j => (DateTimeOffset?)DateTimeOffset.UtcNow)
                    .SetProperty(j => j.LockedUntil, j => (DateTimeOffset?)(DateTimeOffset.UtcNow + lease)), ct);

            if (claimed > 0)
                return nextJobId.Value;

            // Job was claimed by another path; try the next one
        }
    }

    /// <summary>
    /// How long the idle loop may sleep: until the next retry or orphaned lease comes due, capped at the
    /// poll interval. Without it a 10-second backoff would quietly stretch to the 30-second poll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only instants still in the future count. One already past is either claimable — and the drain that
    /// just ran would have taken it — or blocked behind a sibling, and waking on it would spin.
    /// </para>
    /// <para>
    /// The two queries mirror the arms of <see cref="IsClaimable"/> with the time comparison flipped; keep
    /// them in step with it. The wait is measured against the database's clock too, so an app host whose
    /// clock disagrees with Postgres neither wakes early and spins nor sleeps past the due instant.
    /// </para>
    /// </remarks>
    public async Task<TimeSpan> GetIdleDelayAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nextRetry = await db.ProcessingJobs
            .Where(j => j.Status == ProcessingJobStatus.Pending &&
                        j.RunAfter != null && j.RunAfter > DateTimeOffset.UtcNow)
            .MinAsync(j => j.RunAfter, ct);

        var nextLeaseExpiry = await db.ProcessingJobs
            .Where(j => j.Status == ProcessingJobStatus.InProgress && j.TotalChildCount == 0 &&
                        j.LockedUntil != null && j.LockedUntil > DateTimeOffset.UtcNow)
            .MinAsync(j => j.LockedUntil, ct);

        DateTimeOffset? due = (nextRetry, nextLeaseExpiry) switch
        {
            ({ } a, { } b) => a < b ? a : b,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null,
        };

        if (due is null)
            return _pollInterval;

        var dbNow = await db.Database
            .SqlQuery<DateTimeOffset>($"SELECT now() AS \"Value\"")
            .SingleAsync(ct);

        var untilDue = due.Value - dbNow + WakeSlack;
        if (untilDue < WakeSlack)
            return WakeSlack;

        return untilDue < _pollInterval ? untilDue : _pollInterval;
    }

    /// <summary>Runs a job this process has just claimed. Never throws.</summary>
    public async Task RunClaimedJobAsync(Guid jobId, CancellationToken ct)
    {
        // Default until the row is read: a failure before then fences on an instant no claim ever wrote,
        // so nothing is recorded and the lease is left to expire — the job is reclaimed, attempt uncounted.
        DateTimeOffset claimedAt = default;
        Exception? failure;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.ProcessingJobs.FirstAsync(j => j.Id == jobId, CancellationToken.None);
            claimedAt = job.StartedAt ?? default;

            var handler = ResolveHandler(scope.ServiceProvider, job.Type);
            if (handler is null)
            {
                // Known gap, left alone: this fails the job without touching its parent at all — no
                // progress update and no dependent-sibling cancellation. Unreachable in practice, since a
                // job type with no registered handler cannot have been enqueued by this build.
                logger.LogError("No handler registered for job type {JobType}", job.Type);
                var noHandlerError = $"No handler registered for job type '{job.Type}'";
                await db.ProcessingJobs
                    .Where(HeldBy(jobId, claimedAt))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, ProcessingJobStatus.Failed)
                        .SetProperty(j => j.Error, noHandlerError)
                        .SetProperty(j => j.CompletedAt, j => (DateTimeOffset?)DateTimeOffset.UtcNow)
                        .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);
                return;
            }

            job.MaxAttempts = EffectiveMaxAttempts(handler);

            // The handler's token: host shutdown, the per-job timeout, or a lease we can no longer defend.
            using var leaseLost = new CancellationTokenSource();
            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct, leaseLost.Token);
            var timeout = ResolveTimeout(handler, job);
            jobCts.CancelAfter(timeout);

            using var heartbeatStop = new CancellationTokenSource();
            var heartbeat = HeartbeatAsync(jobId, claimedAt, leaseLost, heartbeatStop.Token);

            try
            {
                await handler.HandleAsync(job, jobCts.Token);
                failure = null;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                // Stopped before ANY outcome write. The status guard alone cannot stop a late tick: a parent
                // stays InProgress after its handler returns, and a tick landing after the lease was cleared
                // would re-arm it — making a parent that is only waiting on its children look reclaimable.
                await heartbeatStop.CancelAsync();
                await heartbeat;
            }

            if (failure is null)
            {
                await RecordSuccessAsync(db, job, handler, claimedAt, scope.ServiceProvider);
                return;
            }

            // A graceful stop is not a failed attempt — whatever it surfaced as, since cancellation during
            // shutdown often arrives wrapped. Handing the row straight back keeps a deploy from burning an
            // attempt or parking the job until its lease expires.
            if (ct.IsCancellationRequested)
            {
                await ReleaseAsync(jobId, claimedAt);
                return;
            }

            // Another runner may legitimately hold this job now. Recording anything would describe a run
            // that is no longer ours; the row belongs to whoever reclaims it.
            if (leaseLost.IsCancellationRequested)
            {
                logger.LogWarning(failure,
                    "Job {JobId} ({JobType}) lost its lease; its outcome is left to whoever reclaims it",
                    jobId, job.Type);
                return;
            }

            if (failure is OperationCanceledException && jobCts.IsCancellationRequested)
                failure = new JobTimeoutException(jobId, timeout);
        }
        catch (Exception ex)
        {
            // The bookkeeping itself threw (the row vanished, the success write failed). While the row is
            // still ours and InProgress, that is a failure of the job, recorded like any other. If the
            // Completed write had already committed, HandleFailureAsync instead re-runs the parent tally
            // that was interrupted.
            failure = ex;
        }

        await HandleFailureAsync(jobId, claimedAt, failure);
    }

    /// <summary>
    /// The budget this run gets: the handler's own when it declares a positive one within
    /// <see cref="MaxDeclaredTimeout"/>, else
    /// <see cref="JobProcessingOptions.JobTimeout"/>. A handler that throws while answering gets the global
    /// budget rather than failing the job — the question is about how long to wait, not whether to run.
    /// </summary>
    private TimeSpan ResolveTimeout(IJobHandler handler, ProcessingJob job)
    {
        try
        {
            var declared = handler.ResolveTimeout(job);
            if (declared is null || declared <= TimeSpan.Zero)
                return _jobTimeout;

            // CancelAfter throws past ~49.7 days, which would fail the job; nothing legitimate asks for that.
            if (declared > MaxDeclaredTimeout)
            {
                logger.LogWarning(
                    "Job {JobId} ({JobType}) declared a timeout of {Declared}, over the {Max} ceiling; using the default of {Timeout}",
                    job.Id, job.Type, declared, MaxDeclaredTimeout, _jobTimeout);
                return _jobTimeout;
            }

            return declared.Value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not resolve the timeout for job {JobId} ({JobType}); using the default of {Timeout}",
                job.Id, job.Type, _jobTimeout);
            return _jobTimeout;
        }
    }

    private async Task HeartbeatAsync(
        Guid jobId, DateTimeOffset claimedAt, CancellationTokenSource leaseLost, CancellationToken stop)
    {
        var lease = _leaseDuration;

        // App clock, used only to judge how long a run of failed heartbeats can go on: the lease itself is
        // always written and compared by the database.
        var lastKnownExpiry = DateTimeOffset.UtcNow + lease;

        try
        {
            using var timer = new PeriodicTimer(_heartbeatInterval);

            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    // Its own scope: the handler is using its scoped DbContext concurrently, and DbContext
                    // is not thread-safe.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var extended = await db.ProcessingJobs
                        .Where(HeldBy(jobId, claimedAt))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(j => j.LockedUntil, j => (DateTimeOffset?)(DateTimeOffset.UtcNow + lease)),
                            stop);

                    if (extended == 0)
                    {
                        // Reclaimed by someone else (StartedAt moved) or finished out from under us.
                        logger.LogWarning("Lost the lease on job {JobId}; cancelling its handler", jobId);
                        await leaseLost.CancelAsync();
                        return;
                    }

                    lastKnownExpiry = DateTimeOffset.UtcNow + lease;
                }
                catch (Exception ex) when (!stop.IsCancellationRequested)
                {
                    // One missed beat is not a lost lease: the last good one still has time on it. Give up
                    // only once it could have expired, because from then on another runner may legitimately
                    // claim this job and carrying on would run it twice.
                    if (ShouldGiveUpLease(DateTimeOffset.UtcNow, lastKnownExpiry, _heartbeatInterval))
                    {
                        logger.LogError(ex, "Could not extend the lease on job {JobId}; cancelling its handler", jobId);
                        await leaseLost.CancelAsync();
                        return;
                    }

                    logger.LogWarning(ex, "Could not extend the lease on job {JobId}; retrying on the next beat", jobId);
                }
            }
        }
        catch (Exception ex) when (stop.IsCancellationRequested)
        {
            // The handler finished. A beat that was in flight when it did may still fail for real (a reset
            // connection, not a cancellation); that says nothing about the job, so it must not escape into
            // the runner's `await heartbeat` and turn a successful run into a recorded failure.
            if (ex is not OperationCanceledException)
                logger.LogDebug(ex, "A heartbeat for job {JobId} failed after its handler had finished", jobId);
        }
    }

    private async Task RecordSuccessAsync(
        AppDbContext db, ProcessingJob job, IJobHandler handler, DateTimeOffset claimedAt, IServiceProvider sp)
    {
        // CancellationToken.None throughout the outcome writes: by this point the work is done, and
        // abandoning the bookkeeping would leave the row to be reclaimed and re-run.
        if (handler is IParentJobHandler)
        {
            // A parent waiting on its children holds no lease: nothing is running on its behalf, and a NULL
            // lease is what keeps IsClaimable from ever taking it back.
            job.LockedUntil = null;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation(
                "Parent job {JobId} ({JobType}) spawned {ChildCount} children",
                job.Id, job.Type, job.TotalChildCount);

            // A parent that spawned zero children has nothing to trigger finalization later,
            // so terminate it now (Completed) — see overview.md.
            if (job.TotalChildCount == 0)
                await UpdateParentProgressAsync(db, job.Id, sp, CancellationToken.None);

            return;
        }

        // Fenced status write and the handler's remaining tracked edits (EntityId, a rewritten payload,
        // MaxAttempts) land together or not at all. The status goes first so a lost lease is known before
        // anything of ours is written over the new holder's row.
        await using (var tx = await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            var completed = await db.ProcessingJobs
                .Where(HeldBy(job.Id, claimedAt))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Completed)
                    .SetProperty(j => j.CompletedAt, j => (DateTimeOffset?)DateTimeOffset.UtcNow)
                    .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
                    // A run that succeeded has no error. Left alone, the message from an earlier failed
                    // attempt would sit on a green row reading like a failure.
                    .SetProperty(j => j.Error, (string?)null), CancellationToken.None);

            if (completed == 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
                logger.LogWarning(
                    "Job {JobId} ({JobType}) finished after losing its lease; its outcome was not recorded",
                    job.Id, job.Type);
                return;
            }

            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }

        if (job.ParentJobId is not null)
            await UpdateParentProgressAsync(db, job.ParentJobId.Value, sp, CancellationToken.None);

        logger.LogInformation("Job {JobId} ({JobType}) completed successfully", job.Id, job.Type);
    }

    /// <summary>Hands a job interrupted by shutdown back to the queue without counting an attempt.</summary>
    private async Task ReleaseAsync(Guid jobId, DateTimeOffset claimedAt)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var released = await db.ProcessingJobs
                .Where(HeldBy(jobId, claimedAt))
                .Where(j => j.TotalChildCount == 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Pending)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);

            if (released == 0)
            {
                // A parent that had already saved its children: it is legitimately waiting on them, so it
                // keeps its status and only gives up the lease.
                await db.ProcessingJobs
                    .Where(HeldBy(jobId, claimedAt))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);
            }

            logger.LogInformation("Job {JobId} was released back to the queue on shutdown", jobId);
        }
        catch (Exception ex)
        {
            // The lease will expire and the job be reclaimed; nothing is lost by failing here.
            logger.LogWarning(ex, "Could not release job {JobId} on shutdown; its lease will expire instead", jobId);
        }
    }

    /// <summary>Records a failed attempt: a scheduled retry, or the job's final failure.</summary>
    private async Task HandleFailureAsync(Guid jobId, DateTimeOffset claimedAt, Exception exception)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.ProcessingJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);

            // This run's own Completed write committed, and it was the parent tally after it that threw.
            // Nothing else will ever re-run that tally — the parent holds no lease, and this may have been
            // its last child — so it is retried here, on a fresh scope, rather than leaving the parent
            // InProgress for good.
            if (row is { Status: ProcessingJobStatus.Completed, ParentJobId: { } parentId } &&
                row.StartedAt == claimedAt)
            {
                logger.LogWarning(exception,
                    "Job {JobId} completed but its parent's progress update failed; retrying it", jobId);
                await UpdateParentProgressAsync(db, parentId, scope.ServiceProvider, CancellationToken.None);
                return;
            }

            // Not ours to record: the lease was lost and the job reclaimed (StartedAt moved), or the job
            // already reached its outcome and it was the bookkeeping after that write which threw.
            if (row is null || row.Status != ProcessingJobStatus.InProgress || row.StartedAt != claimedAt)
            {
                logger.LogWarning(exception,
                    "Job {JobId} failed after it was no longer this run's to record ({Status}); nothing was written",
                    jobId, row?.Status);
                return;
            }

            var handler = ResolveHandler(scope.ServiceProvider, row.Type);

            var maxAttempts = EffectiveMaxAttempts(handler);
            var attempts = row.Attempts + 1;

            // Parents never retry once they have children: re-running the handler would re-plan work whose
            // children are already queued. The job's own timeout is never retried and never reaches the
            // handler's ShouldRetry, whatever an override would say about a TimeoutException.
            var retry = handler is not null &&
                        row.TotalChildCount == 0 &&
                        attempts < maxAttempts &&
                        exception is not JobTimeoutException &&
                        ShouldRetry(handler, row, exception);

            if (retry)
            {
                await ScheduleRetryAsync(db, row, claimedAt, attempts, maxAttempts, exception);
                return;
            }

            logger.LogError(exception,
                "Job {JobId} ({JobType}) failed permanently after {Attempts} attempt(s)",
                jobId, row.Type, attempts);

            var cancelledSiblings = await FailAndCancelDependentsAsync(
                db, row, claimedAt, attempts, maxAttempts, exception);

            if (cancelledSiblings is null)
            {
                logger.LogWarning("Job {JobId} lost its lease before its failure could be recorded", jobId);
                return;
            }

            string? errorCode = null;
            Dictionary<string, string>? errorData = null;
            if (exception is IStructuredJobError structured)
            {
                errorCode = structured.ErrorCode;
                errorData = structured.ErrorData;
            }

            var notifier = scope.ServiceProvider.GetRequiredService<IProcessingNotifier>();
            await notifier.NotifyJobStatusAsync(
                row.Id, ProcessingJobStatus.Failed,
                row.EntityType, row.EntityId,
                documentName: null, error: exception.Message,
                row.CreatedByUserId, CancellationToken.None,
                errorCode: errorCode, errorData: errorData);

            if (row.ParentJobId is not null)
            {
                if (cancelledSiblings > 0)
                    logger.LogWarning(
                        "Job {JobId} failed and cancelled {Count} dependent sibling(s) at lower priority",
                        row.Id, cancelledSiblings);

                await UpdateParentProgressAsync(db, row.ParentJobId.Value, scope.ServiceProvider, CancellationToken.None);
            }

            await RunFinalFailureHookAsync(jobId, exception);
        }
        catch (Exception innerEx)
        {
            logger.LogError(innerEx, "Failed to record the failure of job {JobId}", jobId);
        }
    }

    private bool ShouldRetry(IJobHandler handler, ProcessingJob row, Exception exception)
    {
        try
        {
            return handler.ShouldRetry(exception);
        }
        catch (Exception classifierFailure)
        {
            // A classifier that throws cannot vouch for a retry. Failing the job surfaces the real error.
            logger.LogError(classifierFailure,
                "ShouldRetry threw for job {JobId} ({JobType}); treating the failure as final", row.Id, row.Type);
            return false;
        }
    }

    /// <summary>
    /// Puts a failed attempt back in the queue behind its backoff. Deliberately quiet: no notification,
    /// no parent progress, no sibling cancellation.
    /// </summary>
    /// <remarks>
    /// Nothing is announced because the frontend reads a Failed message as final — <c>waitForJob</c>
    /// rejects on it — and a job that is going to be retried has not failed yet. The retrying row is
    /// Pending, so it keeps blocking its lower-priority siblings exactly as it did while it ran, and the
    /// parent's tally is untouched until the job reaches an outcome that counts.
    /// </remarks>
    private async Task ScheduleRetryAsync(
        AppDbContext db, ProcessingJob row, DateTimeOffset claimedAt, int attempts, int maxAttempts,
        Exception exception)
    {
        var delay = JobBackoff.ForAttempt(attempts, _retryBackoff);
        var error = exception.Message;

        var scheduled = await db.ProcessingJobs
            .Where(HeldBy(row.Id, claimedAt))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ProcessingJobStatus.Pending)
                .SetProperty(j => j.Attempts, attempts)
                .SetProperty(j => j.MaxAttempts, maxAttempts)
                .SetProperty(j => j.RunAfter, j => (DateTimeOffset?)(DateTimeOffset.UtcNow + delay))
                .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(j => j.Error, error), CancellationToken.None);

        if (scheduled == 0)
        {
            logger.LogWarning(exception,
                "Job {JobId} lost its lease before its retry could be scheduled", row.Id);
            return;
        }

        logger.LogWarning(exception,
            "Job {JobId} ({JobType}) failed on attempt {Attempt} of {MaxAttempts}; retrying in {Delay}",
            row.Id, row.Type, attempts, maxAttempts, delay);
    }

    /// <summary>
    /// Marks the job Failed and, in the same transaction, cancels the pending siblings that depended on
    /// it — the ones at strictly lower priority under the same parent. Returns how many were cancelled,
    /// or null if the job's lease had already been lost and nothing was written.
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
    /// Only the <b>final</b> failure gets here. A failure that will be retried leaves the job Pending,
    /// which keeps its dependents blocked rather than cancelled — they may yet get to run.
    /// </para>
    /// <para>
    /// If the transaction never commits, the child stays <c>InProgress</c> under a lease nobody renews;
    /// once it expires the ordinary claim reclaims the job and it re-runs, the lost attempt uncounted.
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
    /// finish, and the host's token may already be cancelled by shutdown.
    /// </para>
    /// </remarks>
    private static async Task<int?> FailAndCancelDependentsAsync(
        AppDbContext db, ProcessingJob failedJob, DateTimeOffset claimedAt, int attempts, int maxAttempts,
        Exception ex)
    {
        var error = ex.Message;

        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);

        var failed = await db.ProcessingJobs
            .Where(HeldBy(failedJob.Id, claimedAt))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ProcessingJobStatus.Failed)
                .SetProperty(j => j.Error, error)
                .SetProperty(j => j.CompletedAt, j => (DateTimeOffset?)DateTimeOffset.UtcNow)
                .SetProperty(j => j.Attempts, attempts)
                .SetProperty(j => j.MaxAttempts, maxAttempts)
                .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), CancellationToken.None);

        if (failed == 0)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return null;
        }

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
                    .SetProperty(j => j.CompletedAt, j => (DateTimeOffset?)DateTimeOffset.UtcNow), CancellationToken.None);

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

    /// <summary>Gives the handler its one chance to react to the job's final failure.</summary>
    /// <remarks>
    /// Runs after the Failed row has committed and been announced, so nothing the hook does — or fails to
    /// do — can change the job's recorded outcome. Its own failure is logged and swallowed.
    /// </remarks>
    private async Task RunFinalFailureHookAsync(Guid jobId, Exception exception)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var snapshot = await db.ProcessingJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
            if (snapshot is null)
                return;

            var handler = ResolveHandler(scope.ServiceProvider, snapshot.Type);
            if (handler is null)
                return;

            using var hookCts = new CancellationTokenSource(HookTimeout);
            await handler.OnFinallyFailedAsync(snapshot, exception, hookCts.Token);
        }
        catch (Exception hookFailure)
        {
            logger.LogError(hookFailure, "The final-failure hook for job {JobId} threw", jobId);
        }
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
    // A child waiting on a retry is Pending, so it counts as neither and the parent keeps waiting for it.
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
                    .SetProperty(j => j.CompletedAt, j => (DateTimeOffset?)DateTimeOffset.UtcNow)
                    .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
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
}
