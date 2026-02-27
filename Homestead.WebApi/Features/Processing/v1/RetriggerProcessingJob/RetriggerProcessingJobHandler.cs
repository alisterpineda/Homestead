using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Processing.v1.RetriggerProcessingJob;

public class RetriggerProcessingJobHandler(AppDbContext db, IJobQueue jobQueue, ICurrentUserAccessor currentUser)
    : ICommandHandler<RetriggerProcessingJobRequest, RetriggerProcessingJobResponse?>
{
    public async Task<RetriggerProcessingJobResponse?> HandleAsync(
        RetriggerProcessingJobRequest request, CancellationToken ct)
    {
        var existingJob = await db.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.Id, ct);

        if (existingJob is null)
            return null;

        var userId = currentUser.UserId;

        var newJobId = await jobQueue.EnqueueAsync(existingJob.Type, opts =>
        {
            opts.EntityType = existingJob.EntityType;
            opts.EntityId = existingJob.EntityId;
            opts.Payload = existingJob.Payload;
            opts.Priority = existingJob.Priority;
            opts.CreatedByUserId = userId;
        }, ct);

        return new RetriggerProcessingJobResponse { JobId = newJobId };
    }
}
