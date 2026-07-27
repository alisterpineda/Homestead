using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.RunBackupNow;

/// <summary>
/// Enqueues an ad-hoc run of one backup pairing, mirroring the price-source <c>POST …/runs</c> shape:
/// the endpoint returns a job id and the work happens on the queue.
/// </summary>
/// <remarks>
/// This is where "unavailable with reason" stops being decoration and becomes enforcement. An admin
/// pressing "Back up now" deserves the reason in the response, not a job that turns red a second later —
/// so every condition the planner would skip or fail on is checked here first.
/// </remarks>
public class RunBackupNowHandler(
    AppDbContext db,
    IBackupSourceCatalog catalog,
    IBackupTargetResolver resolver,
    IBackupTargetGate gate,
    IJobQueue queue,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<RunBackupNowCommand, RunBackupNowResponse>
{
    public async Task<RunBackupNowResponse> Handle(RunBackupNowCommand request, CancellationToken ct)
    {
        var source = await db.BackupSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.BackupSourceId, ct)
            ?? throw new EntityNotFoundException("Backup source not found.");

        var descriptor = catalog.Find(source.SourceKind)
            ?? throw new BusinessRuleException(
                $"Backup source kind '{source.SourceKind}' is no longer available in this build.");

        var target = resolver.Resolve(source.TargetKey);

        var verdict = await gate.CheckAsync(target, ct);
        if (!verdict.IsAvailable)
            throw new BusinessRuleException(
                $"Backup target '{source.TargetKey}' is unavailable: {verdict.Reason}");

        if (!source.IsEnabled)
            throw new BusinessRuleException("This backup is disabled.");

        var targetState = await db.BackupTargetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TargetKey == source.TargetKey, ct);

        if (targetState is { IsEnabled: false })
            throw new BusinessRuleException($"Backup target '{source.TargetKey}' is disabled.");

        // ParentJobId == null: batch children share their parent's EntityId, so without it every manual
        // run after the first real one would see leftover children and 409 forever.
        var alreadyQueued = await db.ProcessingJobs
            .AnyAsync(j =>
                j.Type == descriptor.JobType &&
                j.EntityId == source.Id &&
                j.ParentJobId == null &&
                (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.InProgress), ct);

        if (alreadyQueued)
            throw new EntityInUseException("A backup run for this source is already queued.");

        var payload = new BackupRunPayload(source.Id, request.DryRun, request.AllowLargePrune);

        var jobId = await queue.EnqueueAsync(descriptor.JobType, opts =>
        {
            // Stamped here (not only by the handler at wake) so the single-flight check above sees a
            // manual run the instant it is queued, not once its handler starts. BOTH stamping sites are
            // needed: a SCHEDULED run has no enqueue site of its own — the scheduler copies only the
            // payload — so the planner stamps at wake.
            opts.EntityType = ProcessingEntityType.BackupSource;
            opts.EntityId = source.Id;
            opts.Payload = JsonSerializer.Serialize(payload);
            opts.CreatedByUserId = currentUser.UserId;
        }, ct);

        return new RunBackupNowResponse(jobId);
    }
}
