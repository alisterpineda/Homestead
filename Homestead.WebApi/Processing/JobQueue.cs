using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Processing;

public class JobQueue(AppDbContext db, IJobSignal signal, IUnitOfWork unitOfWork) : IJobQueue
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
            CreatedByUserId = options.CreatedByUserId,
            ParentJobId = options.ParentJobId,
            ScheduleId = options.ScheduleId
        };

        db.ProcessingJobs.Add(job);
        await unitOfWork.EnsureTransactionAsync(ct);   // in-command: open the lazy tx so the row flushes INTO it
        await db.SaveChangesAsync(ct);   // standalone scheduler scope: no-op above, this autocommits

        // The processor reads committed rows from a SEPARATE scope/connection, so the wakeup must fire
        // only once our work is durable. Inside a request transaction (the command pipeline) the save
        // above only flushed — defer the signal to after-commit, drained by ExecuteInTransactionAsync.
        // Outside a transaction (the scheduler's own scope) the save already committed, so signal now.
        if (db.Database.CurrentTransaction is not null)
            unitOfWork.OnAfterCommit(_ => { signal.Signal(); return Task.CompletedTask; });
        else
            signal.Signal();

        return job.Id;
    }
}
