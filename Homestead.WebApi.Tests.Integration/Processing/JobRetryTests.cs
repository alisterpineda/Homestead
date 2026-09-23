using System.Net;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Tests.Integration.Processing;

/// <summary>
/// Opt-in retries: a failed attempt of a job type that allows more goes back in the queue behind its
/// backoff, quietly; only the last one fails the job, cancels its dependents, and calls the final-failure
/// hook. Driven through the test NoOp handler, which allows three attempts.
/// </summary>
[Collection("Integration")]
public class JobRetryTests : JobRunnerTestBase
{
    public JobRetryTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ATransientFailure_IsRetried_ThenSucceeds()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(FailTimes: 1)));

        Assert.True(await Runner.RunOnceAsync(CT));

        var retrying = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Pending, retrying.Status);
        Assert.Equal(1, retrying.Attempts);
        Assert.Equal(3, retrying.MaxAttempts);
        Assert.NotNull(retrying.RunAfter);

        // The first backoff entry (200ms), not the second (an hour): the index is attempts-failed-so-far.
        Assert.True(retrying.RunAfter < DateTimeOffset.UtcNow.AddMinutes(1),
            $"Expected the first backoff entry, got RunAfter {retrying.RunAfter}.");
        Assert.Null(retrying.StartedAt);
        Assert.Null(retrying.LockedUntil);
        Assert.Null(retrying.CompletedAt);
        Assert.Contains("Transient failure on attempt 1", retrying.Error);

        // The frontend reads a Failed message as final, and this job has not failed yet.
        Assert.DoesNotContain(ProcessingJobStatus.Failed, Notifications.StatusesFor(jobId));

        await MakeDueAsync(jobId);
        Assert.True(await Runner.RunOnceAsync(CT));

        var done = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Completed, done.Status);

        // Attempts counts runs that ended in an exception, so the one that succeeded adds nothing.
        Assert.Equal(1, done.Attempts);

        // A green row carries no error, whatever an earlier attempt left there.
        Assert.Null(done.Error);
        Assert.Null(done.LockedUntil);
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.CompletedAt);
        Assert.Empty(Probe.CallsFor(jobId));
    }

    [Fact]
    public async Task AJobWhoseRetryIsNotYetDue_IsNotClaimed()
    {
        await using var _ = await PauseAsync();
        var job = NoOp();
        job.RunAfter = DateTimeOffset.UtcNow.AddMinutes(10);
        var jobId = await SeedAsync(job);

        Assert.False(await Runner.RunOnceAsync(CT));
        Assert.Equal(ProcessingJobStatus.Pending, (await ReadAsync(jobId)).Status);
    }

    [Fact]
    public async Task WhenAttemptsRunOut_TheJobFailsOnce_AndTheHookRunsOnce()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(FailTimes: int.MaxValue)));

        Assert.True(await Runner.RunOnceAsync(CT));
        await MakeDueAsync(jobId);
        Assert.True(await Runner.RunOnceAsync(CT));

        // Two attempts in, one to go: still retrying, and neither the hook nor the frontend has been told
        // anything.
        var retrying = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Pending, retrying.Status);
        Assert.Empty(Probe.CallsFor(jobId));
        Assert.DoesNotContain(ProcessingJobStatus.Failed, Notifications.StatusesFor(jobId));

        // The second backoff entry (an hour) this time, so the retry is not claimable yet.
        Assert.True(retrying.RunAfter > DateTimeOffset.UtcNow.AddMinutes(55),
            $"Expected the second backoff entry, got RunAfter {retrying.RunAfter}.");
        Assert.False(await Runner.RunOnceAsync(CT));

        await MakeDueAsync(jobId);
        Assert.True(await Runner.RunOnceAsync(CT));

        var failed = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Failed, failed.Status);
        Assert.Equal(3, failed.Attempts);
        Assert.Equal(3, failed.MaxAttempts);
        Assert.Null(failed.LockedUntil);
        Assert.NotNull(failed.CompletedAt);
        Assert.Contains("attempt 3", failed.Error);

        var hook = Assert.Single(Probe.CallsFor(jobId));
        Assert.Equal(ProcessingJobStatus.Failed, hook.Status);
        Assert.IsType<HttpRequestException>(hook.Exception);

        // Announced exactly once, on the final failure.
        Assert.Single(Notifications.StatusesFor(jobId), s => s == ProcessingJobStatus.Failed);

        // Nothing left to claim: a Failed row is terminal.
        Assert.False(await Runner.RunOnceAsync(CT));
    }

    [Theory]
    [InlineData(TestNoOpPayload.Permanent)]
    [InlineData(TestNoOpPayload.Structured)]
    public async Task AFailureThatWillRecur_FailsOnTheFirstAttempt(string failWith)
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(FailTimes: int.MaxValue, FailWith: failWith)));

        Assert.True(await Runner.RunOnceAsync(CT));

        var failed = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Failed, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Single(Probe.CallsFor(jobId));
    }

    /// <summary>
    /// A retrying child is Pending, so it keeps blocking the siblings below it exactly as it did while it
    /// ran, and the parent's tally does not move. Only the final failure cancels the dependents and lets
    /// the parent finalize — a failure that might still come good must not tear down work that depends on it.
    /// </summary>
    [Fact]
    public async Task ARetryingChild_KeepsItsDependentsBlocked_UntilItsFinalFailureCancelsThem()
    {
        await using var _ = await PauseAsync();

        var parentId = await SeedAsync(ParentWaitingOn(childCount: 2));
        var failingId = await SeedAsync(NoOp(new TestNoOpPayload(FailTimes: int.MaxValue), parentJobId: parentId, priority: 0));
        var dependentId = await SeedAsync(NoOp(parentJobId: parentId, priority: -1));

        Assert.True(await Runner.RunOnceAsync(CT));

        Assert.Equal(ProcessingJobStatus.Pending, (await ReadAsync(failingId)).Status);
        Assert.Equal(ProcessingJobStatus.Pending, (await ReadAsync(dependentId)).Status);

        var waiting = await ReadAsync(parentId);
        Assert.Equal(ProcessingJobStatus.InProgress, waiting.Status);
        Assert.Equal(2, waiting.TotalChildCount);
        Assert.Equal(0, waiting.FailedChildCount);

        // While its blocker waits out a backoff, the dependent is not claimable: the failing child is not
        // due, so nothing else would win the claim ahead of the dependent if the dependent were free.
        await UpdateAsync(failingId, s => s.SetProperty(j => j.RunAfter, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.False(await Runner.RunOnceAsync(CT));
        Assert.Equal(ProcessingJobStatus.Pending, (await ReadAsync(dependentId)).Status);

        for (var attempt = 2; attempt <= 3; attempt++)
        {
            await MakeDueAsync(failingId);
            Assert.True(await Runner.RunOnceAsync(CT));
        }

        Assert.Equal(ProcessingJobStatus.Failed, (await ReadAsync(failingId)).Status);
        Assert.Equal(ProcessingJobStatus.Cancelled, (await ReadAsync(dependentId)).Status);

        var parent = await ReadAsync(parentId);
        Assert.Equal(ProcessingJobStatus.Failed, parent.Status);
        Assert.Equal(1, parent.TotalChildCount);
        Assert.Equal(1, parent.FailedChildCount);
    }

    /// <summary>
    /// A handler still running at <c>JobTimeout</c> is cancelled, and that surfaces as a
    /// <see cref="JobTimeoutException"/>, not the <see cref="OperationCanceledException"/> the handler saw,
    /// which the default classifier would read as a transient HttpClient timeout. A job that needed longer
    /// than its budget will need longer again, so it fails on the first attempt despite allowing three.
    /// </summary>
    [Fact]
    public async Task AJobThatTimesOut_FailsWithoutARetry()
    {
        await using var _ = await PauseAsync();
        var configured = Factory.Services.GetRequiredService<IOptions<JobProcessingOptions>>().Value;
        var runner = new JobRunner(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobProcessingOptions
            {
                JobTimeout = TimeSpan.FromMilliseconds(300),
                LeaseDuration = configured.LeaseDuration,
                HeartbeatInterval = configured.HeartbeatInterval,
                PollInterval = configured.PollInterval,
                RetryBackoff = configured.RetryBackoff,
            }),
            Factory.Services.GetRequiredService<ILogger<JobRunner>>());

        // Blocks until cancelled: the gate is never released.
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(Block: true)));

        Assert.True(await runner.RunOnceAsync(CT).WaitAsync(TimeSpan.FromSeconds(10), CT));

        var failed = await ReadAsync(jobId);
        Assert.Equal(ProcessingJobStatus.Failed, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("timed out", failed.Error);

        var hook = Assert.Single(Probe.CallsFor(jobId));
        Assert.IsType<JobTimeoutException>(hook.Exception);
    }

    [Fact]
    public async Task AHookThatThrows_IsSwallowed()
    {
        await using var _ = await PauseAsync();
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(
            FailTimes: int.MaxValue, FailWith: TestNoOpPayload.Permanent, HookThrows: true)));

        Assert.True(await Runner.RunOnceAsync(CT));

        Assert.Equal(ProcessingJobStatus.Failed, (await ReadAsync(jobId)).Status);
        Assert.Single(Probe.CallsFor(jobId));
    }

    /// <summary>
    /// Unpaused: the background loop itself has to wake for the retry. The deadline is well under the 30s
    /// poll, so passing proves the loop slept only until the retry came due, not until the next poll.
    /// </summary>
    [Fact]
    public async Task TheBackgroundLoop_WakesWhenARetryComesDue()
    {
        var jobId = await SeedAsync(NoOp(new TestNoOpPayload(FailTimes: 1)));
        Signal();

        await WaitForJobCompletionAsync(jobId, TimeSpan.FromSeconds(10));

        Assert.Equal(1, (await ReadAsync(jobId)).Attempts);
    }

    private static ProcessingJob ParentWaitingOn(int childCount)
    {
        var parent = NoOp(status: ProcessingJobStatus.InProgress);
        parent.TotalChildCount = childCount;
        return parent;
    }
}
