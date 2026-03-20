using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class JobQueueTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IJobSignal _signal;
    private readonly JobQueue _queue;

    public JobQueueTests()
    {
        _db = TestAppDbContext.Create();
        _signal = Substitute.For<IJobSignal>();
        _queue = new JobQueue(_db, _signal);
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
