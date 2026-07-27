using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Processing;

/// <summary>
/// The sibling happens-before rule: among ONE parent's children, a strictly higher <c>Priority</c> means
/// "runs before", not merely "claimed sooner". Equal priority imposes no edge.
/// </summary>
/// <remarks>
/// These have to be integration tests. The claim path is built on <c>ExecuteUpdateAsync</c>, which EF
/// InMemory does not implement — the reason <c>JobProcessorServiceTests</c> stands its SUT up on an empty
/// <c>ServiceCollection</c> and covers only the semaphore surface. There is also no per-test config seam
/// (the host is a collection-wide singleton at <c>MaxConcurrency: 4</c>), which is fine: every assertion
/// here is about <b>claimability</b>, never about execution timing.
/// </remarks>
[Collection("Integration")]
public class JobClaimOrderingTests : IntegrationTestBase
{
    public JobClaimOrderingTests(PostgresFixture fixture) : base(fixture) { }

    // ---------------------------------------------------------------------------------------------
    // Claim ordering
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The whole point of the rule. A low-priority child stays Pending for as long as a higher-priority
    /// sibling is outstanding — here one parked InProgress, standing in for a batch mid-flight. Only when
    /// that blocker terminates does the low child become claimable. Before the fix the low child was
    /// claimed immediately, which is what let a backup Put overtake the Move it depended on.
    /// </summary>
    [Fact]
    public async Task AChildIsNotClaimedWhileAHigherPrioritySiblingIsOutstanding()
    {
        var control = Factory.Services.GetRequiredService<IJobProcessorControl>();

        Guid highId;
        Guid lowId;

        await using (await control.PauseAsync(CT))
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parent = Parent(totalChildCount: 2);
            var high = Child(parent, priority: -100, ProcessingJobStatus.InProgress);
            var low = Child(parent, priority: -101);

            db.ProcessingJobs.AddRange(parent, high, low);
            await db.SaveChangesAsync(CT);

            highId = high.Id;
            lowId = low.Id;
        }

        Signal();

        // Bounded interval: long enough that a processor willing to claim the low child would have done
        // so many times over, short enough not to slow the suite.
        await Task.Delay(TimeSpan.FromSeconds(3), CT);
        Assert.Equal(ProcessingJobStatus.Pending, await StatusOfAsync(lowId));

        // Terminate the blocker and the edge is discharged.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ProcessingJobs
                .Where(j => j.Id == highId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Completed)
                    .SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow), CT);
        }

        Signal();

        Assert.Equal(ProcessingJobStatus.Completed, await WaitForStatusAsync(lowId, ProcessingJobStatus.Completed));
    }

    /// <summary>
    /// Equal priority imposes no edge, so an ordinary flat fan-out — every child at the default 0, as
    /// bulk classify and bulk reprocess emit — keeps running fully concurrent. This is the guard against
    /// the fix quietly serializing every parent job in the system.
    /// </summary>
    [Fact]
    public async Task SiblingsAtEqualPriorityAreNotBlocked()
    {
        var control = Factory.Services.GetRequiredService<IJobProcessorControl>();

        Guid firstId;
        Guid secondId;

        await using (await control.PauseAsync(CT))
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parent = Parent(totalChildCount: 2);
            var first = Child(parent, priority: 0);
            var second = Child(parent, priority: 0);

            db.ProcessingJobs.AddRange(parent, first, second);
            await db.SaveChangesAsync(CT);

            firstId = first.Id;
            secondId = second.Id;
        }

        Signal();

        await WaitForStatusAsync(firstId, ProcessingJobStatus.Completed);
        await WaitForStatusAsync(secondId, ProcessingJobStatus.Completed);
    }

    /// <summary>
    /// The regression guard for the EF null-compensation trap. The claim predicate's
    /// <c>j.ParentJobId == null</c> arm is load-bearing: EF expands <c>s.ParentJobId == j.ParentJobId</c>
    /// with an <c>IS NULL AND IS NULL</c> arm, so without that guard every parentless job would fall into
    /// one giant "sibling group" and the higher-priority root would serialize the entire queue — this test
    /// would hang on the low job forever.
    /// </summary>
    [Fact]
    public async Task ARootJobIsNotBlockedByAnotherRootJob()
    {
        var control = Factory.Services.GetRequiredService<IJobProcessorControl>();

        Guid highId;
        Guid lowId;

        await using (await control.PauseAsync(CT))
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Parentless, and deliberately at different priorities. The high one is left InProgress —
            // exactly the shape that would block a true sibling — while the low one must claim anyway.
            var high = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.NoOp,
                Status = ProcessingJobStatus.InProgress,
                Priority = 50,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = DateTimeOffset.UtcNow,
                TotalChildCount = 1,
            };
            var low = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.NoOp,
                Status = ProcessingJobStatus.Pending,
                Priority = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.ProcessingJobs.AddRange(high, low);
            await db.SaveChangesAsync(CT);

            highId = high.Id;
            lowId = low.Id;
        }

        Signal();

        await WaitForStatusAsync(lowId, ProcessingJobStatus.Completed);

        // The parked root is untouched — it was never a sibling of anything.
        Assert.Equal(ProcessingJobStatus.InProgress, await StatusOfAsync(highId));
    }

    // ---------------------------------------------------------------------------------------------
    // Failure propagation
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A failed child takes its dependents down with it. Only siblings at strictly lower priority are
    /// cancelled — equal-priority siblings are independent by the same rule that lets a flat fan-out run
    /// concurrently, so they are left to finish and <c>PartiallyCompleted</c> stays reachable. The parent's
    /// <c>TotalChildCount</c> drops by the cancelled count rather than the tally learning about Cancelled,
    /// which is what still lets the parent finalize.
    /// </summary>
    [Fact]
    public async Task AFailedChildCancelsOnlyItsLowerPrioritySiblings()
    {
        var control = Factory.Services.GetRequiredService<IJobProcessorControl>();

        Guid parentId;
        Guid failingId;
        Guid peerId;
        Guid lowId;

        await using (await control.PauseAsync(CT))
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parent = Parent(totalChildCount: 4);

            // Highest — a plain NoOp, so the middle tier is reached.
            var top = Child(parent, priority: -100);

            // A ReprocessDocumentVersion with no payload throws before it touches anything: the handler's
            // first statement is a null-payload guard. Deterministic, and needs no fixture document.
            var failing = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.ReprocessDocumentVersion,
                Status = ProcessingJobStatus.Pending,
                ParentJobId = parent.Id,
                Priority = -101,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            // Same priority as the failing child: independent of it, so it must survive and run.
            var peer = Child(parent, priority: -101);

            // Below the failure: dependent, so it must be cancelled without ever running.
            var low = Child(parent, priority: -102);

            db.ProcessingJobs.AddRange(parent, top, failing, peer, low);
            await db.SaveChangesAsync(CT);

            parentId = parent.Id;
            failingId = failing.Id;
            peerId = peer.Id;
            lowId = low.Id;
        }

        Signal();

        await WaitForTerminalAsync(parentId);

        using var assertScope = CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var failed = await assertDb.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == failingId, CT);
        Assert.Equal(ProcessingJobStatus.Failed, failed.Status);

        var cancelled = await assertDb.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == lowId, CT);
        Assert.Equal(ProcessingJobStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);

        var survivor = await assertDb.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == peerId, CT);
        Assert.Equal(ProcessingJobStatus.Completed, survivor.Status);

        var parentRow = await assertDb.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == parentId, CT);

        // Seeded with 4, one cancelled => 3 children were still expected to terminate, and 3 did.
        Assert.Equal(3, parentRow.TotalChildCount);
        Assert.Equal(2, parentRow.CompletedChildCount);
        Assert.Equal(1, parentRow.FailedChildCount);
        Assert.Equal(ProcessingJobStatus.PartiallyCompleted, parentRow.Status);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures and polling
    // ---------------------------------------------------------------------------------------------

    private static ProcessingJob Parent(int totalChildCount) => new()
    {
        Id = Guid.NewGuid(),
        Type = ProcessingJobType.NoOp,
        Status = ProcessingJobStatus.InProgress,
        TotalChildCount = totalChildCount,
        CreatedAt = DateTimeOffset.UtcNow,
        StartedAt = DateTimeOffset.UtcNow,
    };

    private static ProcessingJob Child(
        ProcessingJob parent, int priority, ProcessingJobStatus status = ProcessingJobStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        Type = ProcessingJobType.NoOp,
        Status = status,
        ParentJobId = parent.Id,
        Priority = priority,
        CreatedAt = DateTimeOffset.UtcNow,
        StartedAt = status == ProcessingJobStatus.InProgress ? DateTimeOffset.UtcNow : null,
    };

    private void Signal() => Factory.Services.GetRequiredService<IJobSignal>().Signal();

    private async Task<ProcessingJobStatus> StatusOfAsync(Guid jobId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ProcessingJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, CT);
        return job.Status;
    }

    private async Task<ProcessingJobStatus> WaitForStatusAsync(
        Guid jobId, ProcessingJobStatus expected, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));

        ProcessingJobStatus last;
        do
        {
            last = await StatusOfAsync(jobId);
            if (last == expected)
                return last;

            await Task.Delay(150, CT);
        } while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Job {jobId} was {last}, expected {expected}, within the timeout.");
    }

    private async Task WaitForTerminalAsync(Guid jobId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        ProcessingJobStatus last;
        do
        {
            last = await StatusOfAsync(jobId);
            if (last is ProcessingJobStatus.Completed or ProcessingJobStatus.Failed
                or ProcessingJobStatus.PartiallyCompleted or ProcessingJobStatus.Cancelled)
                return;

            await Task.Delay(150, CT);
        } while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Job {jobId} was still {last} at the deadline; expected a terminal status.");
    }
}
