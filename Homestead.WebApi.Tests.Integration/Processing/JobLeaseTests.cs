using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Processing;

/// <summary>
/// The lease: a claim holds a job for <c>Processing:LeaseDuration</c>, a heartbeat keeps pushing that
/// forward, and a childless job left InProgress with an expired lease — what a crashed process leaves
/// behind — is simply claimable again. There is no startup sweep.
/// </summary>
[Collection("Integration")]
public class JobLeaseTests : JobRunnerTestBase
{
    public JobLeaseTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AnExpiredLease_IsReclaimed_AndTheJobRunsToCompletion()
    {
        await using var _ = await PauseAsync();
        var crashedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var jobId = await SeedAsync(Orphan(startedAt: crashedAt, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.True(await Runner.RunOnceAsync(CT));

        var job = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.True(job.StartedAt > crashedAt, "The reclaim should re-stamp StartedAt.");

        // A reclaim is not a failed run.
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.LockedUntil);
    }

    [Fact]
    public async Task ALiveLease_IsNotReclaimed()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(Orphan(DateTimeOffset.UtcNow, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(10)));

        Assert.False(await Runner.RunOnceAsync(CT));
        Assert.Equal(ProcessingJobStatus.InProgress, (await ReadAsync(jobId)).Status);
    }

    /// <summary>A NULL lease is a parent waiting on its children (or a row parked by hand), never an orphan.</summary>
    [Fact]
    public async Task AnInProgressJobWithNoLease_IsNeverReclaimed()
    {
        await using var _ = await PauseAsync();
        await SeedAsync(Orphan(DateTimeOffset.UtcNow.AddHours(-1), lockedUntil: null));

        Assert.False(await Runner.RunOnceAsync(CT));
    }

    /// <summary>
    /// The guard the startup sweep had, carried over. A parent handler saves its children before the
    /// processor clears the lease; reclaiming it after that would re-run the handler and undo the
    /// decrement for any child already cancelled, and the parent would never finalize.
    /// </summary>
    [Fact]
    public async Task AParentWithChildren_IsNotReclaimed_EvenWithAnExpiredLease()
    {
        await using var _ = await PauseAsync();
        var parent = Orphan(DateTimeOffset.UtcNow.AddMinutes(-10), lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));
        parent.TotalChildCount = 2;
        var parentId = await SeedAsync(parent);

        // Its children are running elsewhere; nothing here is claimable.
        await SeedAsync(Orphan(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), parentId));
        await SeedAsync(Orphan(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), parentId));

        Assert.False(await Runner.RunOnceAsync(CT));
        Assert.Equal(ProcessingJobStatus.InProgress, (await ReadAsync(parentId)).Status);
    }

    [Fact]
    public async Task AParentWaitingOnItsChildren_HoldsNoLease()
    {
        var client = await CreateAdminClientAsync();
        var documentId = await UploadAndWaitAsync(client, "lease-parent.pdf");

        await using var _ = await PauseAsync();
        var bulk = new ProcessingJob
        {
            Type = ProcessingJobType.BulkClassifyDocuments,
            Status = ProcessingJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        bulk.SetPayload(new BulkClassifyPayload([documentId]));
        var parentId = await SeedAsync(bulk);

        Assert.True(await Runner.RunOnceAsync(CT));

        var parent = await ReadAsync(parentId);
        Assert.Equal(ProcessingJobStatus.InProgress, parent.Status);
        Assert.Equal(1, parent.TotalChildCount);
        Assert.Null(parent.LockedUntil);
    }

    [Fact]
    public async Task TheHeartbeat_KeepsPushingTheLeaseForward()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true)));

        var run = Runner.RunOnceAsync(CT);
        try
        {
            await Gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), CT);

            var first = (await ReadAsync(jobId)).LockedUntil;
            Assert.NotNull(first);

            // Several 500ms beats.
            await Task.Delay(TimeSpan.FromSeconds(1.5), CT);

            var later = (await ReadAsync(jobId)).LockedUntil;
            Assert.True(later > first, $"Expected the lease to move past {first}, was {later}.");
        }
        finally
        {
            Gate.Release();
            await run;
        }

        var done = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Completed, done.Status);
        Assert.Null(done.LockedUntil);
    }

    /// <summary>
    /// Moving <c>StartedAt</c> is what a reclaim by another runner looks like from here. The next beat
    /// finds its fence gone, cancels the handler, and the runner writes nothing: the row is the new
    /// holder's now.
    /// </summary>
    [Fact]
    public async Task ALostLease_CancelsTheHandler_AndWritesNothing()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true)));

        var run = Runner.RunOnceAsync(CT);
        await Gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), CT);

        var reclaimedAt = DateTimeOffset.UtcNow.AddSeconds(5);
        await UpdateAsync(jobId, s => s.SetProperty(j => j.StartedAt, reclaimedAt));

        // The handler never gets released: only the lost lease can end this run.
        await run.WaitAsync(TimeSpan.FromSeconds(10), CT);

        var job = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.InProgress, job.Status);
        Assert.Equal(reclaimedAt.ToUnixTimeMilliseconds(), job.StartedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.Error);
        Assert.Empty(Probe.CallsFor(jobId));
    }

    /// <summary>
    /// The success write is fenced too. Here the handler returns normally after the reclaim, so the run
    /// reaches the Completed write, or the next beat notices first. Either way the row must stay the new
    /// holder's.
    /// </summary>
    [Fact]
    public async Task AHandlerThatSucceedsAfterLosingItsLease_RecordsNothing()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true)));

        var run = Runner.RunOnceAsync(CT);
        await Gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), CT);

        var reclaimedAt = DateTimeOffset.UtcNow.AddSeconds(5);
        await UpdateAsync(jobId, s => s.SetProperty(j => j.StartedAt, reclaimedAt));

        Gate.Release();
        await run.WaitAsync(TimeSpan.FromSeconds(10), CT);

        var job = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.InProgress, job.Status);
        Assert.Equal(reclaimedAt.ToUnixTimeMilliseconds(), job.StartedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Null(job.CompletedAt);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.Error);
    }

    /// <summary>
    /// Unpaused, and the lease is still live when the signal-driven drain looks at it, so only the idle
    /// loop's lease-expiry wake can bring it back before the 30s poll.
    /// </summary>
    [Fact]
    public async Task TheBackgroundLoop_WakesWhenAnOrphanedLeaseExpires()
    {
        var jobId = await SeedAsync(Orphan(DateTimeOffset.UtcNow.AddMinutes(-1), lockedUntil: DateTimeOffset.UtcNow.AddSeconds(1)));
        Signal();

        await WaitForJobCompletionAsync(jobId, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Shutdown_ReleasesTheJob_WithoutCountingAnAttempt()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true)));

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(CT);
        var run = Runner.RunOnceAsync(shutdown.Token);
        await Gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), CT);

        await shutdown.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(10), CT);

        var job = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Null(job.StartedAt);
        Assert.Null(job.LockedUntil);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.Error);
        Assert.Empty(Probe.CallsFor(jobId));
    }

    private static ProcessingJob Orphan(DateTimeOffset startedAt, DateTimeOffset? lockedUntil, Guid? parentJobId = null)
    {
        var job = NoOp(status: ProcessingJobStatus.InProgress, parentJobId: parentJobId);
        job.StartedAt = startedAt;
        job.LockedUntil = lockedUntil;
        return job;
    }
}
