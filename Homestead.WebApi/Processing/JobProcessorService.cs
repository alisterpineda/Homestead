using Homestead.WebApi.Common;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Processing;

/// <summary>Drives <see cref="JobRunner"/> for the lifetime of the process.</summary>
/// <remarks>
/// Deliberately only about <i>when</i>: bounded concurrency, the wake-up signal, the idle sleep, and the
/// pause. Everything that happens to a job — claim, lease, retry, outcome — is the runner's, which is
/// what lets tests pause this loop and drive <see cref="JobRunner.RunOnceAsync"/> themselves.
/// </remarks>
public class JobProcessorService(
    JobRunner runner,
    IOptions<JobProcessingOptions> options,
    ILogger<JobProcessorService> logger) : BackgroundService, IJobProcessorControl, IJobSignal
{
    private readonly TimeSpan _pollInterval = options.Value.PollInterval;
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

        // Drain before the first sleep: work left over from before a restart — including anything the last
        // process was holding when it died, once its lease runs out — starts now, not a poll interval late.
        // There is no startup sweep; an expired lease is simply claimable.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainPendingJobsAsync(stoppingToken);
                await WaitForSignalOrTimeoutAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in job processing loop");

                // Claiming itself failed — the database is down, most likely. Back off rather than spin
                // straight back into the same error.
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    private async Task WaitForSignalOrTimeoutAsync(CancellationToken ct)
    {
        await _signal.WaitAsync(await NextWakeAsync(ct), ct);

        // Drain extra accumulated signals to prevent redundant empty DB queries
        while (_signal.Wait(TimeSpan.Zero, CancellationToken.None))
        {
        }
    }

    /// <summary>The runner's next due instant, or the poll interval if it cannot say.</summary>
    private async Task<TimeSpan> NextWakeAsync(CancellationToken ct)
    {
        try
        {
            return await runner.GetIdleDelayAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not compute the next job due time; falling back to the poll interval");
            return _pollInterval;
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
                    jobId = await runner.TryClaimNextJobAsync(ct);
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
                    // claimable job. WhenAny is safe because dispatched tasks never throw — RunClaimedJobAsync
                    // catches everything.
                    if (activeTasks.Count == 0)
                        break;

                    await WaitForFirstTaskOrNextDueAsync(activeTasks, ct);
                    continue;
                }

                var capturedJobId = jobId.Value;
                // CancellationToken.None ensures the task is always scheduled so the
                // finally block releases the semaphore even during shutdown.
                activeTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await runner.RunClaimedJobAsync(capturedJobId, ct);
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

    /// <summary>
    /// The mid-drain wait: until a dispatched job finishes, or a retry comes due, whichever is first.
    /// Without the second arm a retry falling due behind a long backup batch would wait for the batch.
    /// </summary>
    private async Task WaitForFirstTaskOrNextDueAsync(List<Task> activeTasks, CancellationToken ct)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var nextDue = Task.Delay(await NextWakeAsync(ct), delayCts.Token);

        await Task.WhenAny([.. activeTasks, nextDue]);

        // Stop the timer we no longer need; the delay task's cancellation is observed by nobody.
        await delayCts.CancelAsync();
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
