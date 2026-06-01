using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class JobQueueTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IJobSignal _signal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JobQueue _queue;

    public JobQueueTests()
    {
        _db = TestAppDbContext.Create();
        _signal = Substitute.For<IJobSignal>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        // The InMemory provider reports no ambient transaction (CurrentTransaction == null), so the queue
        // takes the "no transaction" branch and signals inline — exactly as the scheduler's own scope does.
        _queue = new JobQueue(_db, _signal, _unitOfWork);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EnqueueAsync_PersistsJobAndSignals()
    {
        var ct = TestContext.Current.CancellationToken;

        var jobId = await _queue.EnqueueAsync(ProcessingJobType.UploadDocument, ct: ct);

        var job = await _db.ProcessingJobs.FindAsync([jobId], ct);
        Assert.NotNull(job);
        Assert.Equal(ProcessingJobStatus.Pending, job!.Status);
        Assert.Equal(ProcessingJobType.UploadDocument, job.Type);

        // The enqueue is a change-tracker-bypassing direct save; in-command the SavingChanges interceptor
        // opens the lazy command tx so it lands atomically (a no-op in the scheduler's own scope, which is
        // what InMemory exercises here — CurrentTransaction stays null, so the signal fires inline).
        _signal.Received(1).Signal();
    }

    [Fact]
    public async Task EnqueueAsync_AppliesConfiguration()
    {
        var ct = TestContext.Current.CancellationToken;
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var jobId = await _queue.EnqueueAsync(ProcessingJobType.UploadDocument, opts =>
        {
            opts.EntityType = ProcessingEntityType.Document;
            opts.EntityId = entityId;
            opts.Priority = 5;
            opts.Payload = "{\"test\":true}";
            opts.CreatedByUserId = userId;
        }, ct);

        var job = await _db.ProcessingJobs.FindAsync([jobId], ct);
        Assert.NotNull(job);
        Assert.Equal(ProcessingEntityType.Document, job!.EntityType);
        Assert.Equal(entityId, job.EntityId);
        Assert.Equal(5, job.Priority);
        Assert.Equal("{\"test\":true}", job.Payload);
        Assert.Equal(userId, job.CreatedByUserId);

        _signal.Received(1).Signal();
    }

    [Fact]
    public async Task EnqueueAsync_MultipleEnqueues_SignalsEachTime()
    {
        var ct = TestContext.Current.CancellationToken;

        await _queue.EnqueueAsync(ProcessingJobType.UploadDocument, ct: ct);
        await _queue.EnqueueAsync(ProcessingJobType.UploadDocument, ct: ct);
        await _queue.EnqueueAsync(ProcessingJobType.UploadDocument, ct: ct);

        _signal.Received(3).Signal();
    }
}
