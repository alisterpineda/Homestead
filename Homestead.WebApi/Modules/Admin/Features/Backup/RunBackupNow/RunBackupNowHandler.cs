using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.RunBackupNow;

/// <summary>
/// Enqueues an ad-hoc backup run, mirroring the price-source <c>POST …/runs</c> shape: the endpoint
/// returns a job id and the work happens on the queue.
/// </summary>
public class RunBackupNowHandler(
    AppDbContext db,
    IBackupTargetResolver resolver,
    IJobQueue queue,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<RunBackupNowCommand, RunBackupNowResponse>
{
    public async Task<RunBackupNowResponse> Handle(RunBackupNowCommand request, CancellationToken ct)
    {
        var target = resolver.All()
            .FirstOrDefault(t => string.Equals(t.Key, request.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new EntityNotFoundException($"Backup target '{request.TargetKey}' is not configured.");

        // Fail at the API rather than letting the job fail later: an admin pressing "Back up now" on a
        // misconfigured target deserves the reason in the response, not a job that turns red a second
        // later.
        if (!target.IsAvailable)
            throw new BusinessRuleException(
                $"Backup target '{target.Key}' is unavailable: {target.ConfigurationError}");

        var state = await db.BackupTargetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TargetKey == target.Key, ct);

        if (state is { IsEnabled: false })
            throw new BusinessRuleException($"Backup target '{target.Key}' is disabled.");

        var entityId = DeterministicGuid.FromKey(target.Key);

        var alreadyQueued = await db.ProcessingJobs
            .AnyAsync(j =>
                j.Type == ProcessingJobType.BackupToTarget &&
                j.EntityId == entityId &&
                (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.InProgress), ct);

        if (alreadyQueued)
            throw new EntityInUseException($"A backup run for '{target.Key}' is already queued.");

        var payload = new BackupToTargetPayload(target.Key, request.DryRun, request.AllowLargePrune);

        var jobId = await queue.EnqueueAsync(ProcessingJobType.BackupToTarget, opts =>
        {
            // Stamped here (not just by the handler) so the single-flight check above sees a manual run
            // the instant it is queued, not only once its handler starts.
            opts.EntityType = ProcessingEntityType.BackupTarget;
            opts.EntityId = entityId;
            opts.Payload = JsonSerializer.Serialize(payload);
            opts.CreatedByUserId = currentUser.UserId;
        }, ct);

        return new RunBackupNowResponse(jobId);
    }
}
