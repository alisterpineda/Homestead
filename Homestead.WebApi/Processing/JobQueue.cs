using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Processing;

public class JobQueue(AppDbContext db, IJobSignal signal) : IJobQueue
{
    public async Task<Guid> EnqueueAsync(ProcessingJobType type, Action<EnqueueJobRequest>? configure = null,
        CancellationToken ct = default)
    {
        var options = new EnqueueJobRequest();
        configure?.Invoke(options);

        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = ProcessingJobStatus.Pending,
            EntityType = options.EntityType,
            EntityId = options.EntityId,
            Payload = options.Payload,
            Priority = options.Priority,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = options.CreatedByUserId
        };

        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);

        signal.Signal();

        return job.Id;
    }
}
