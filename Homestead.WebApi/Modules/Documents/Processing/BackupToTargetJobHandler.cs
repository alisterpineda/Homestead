using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Processing;

/// <summary>
/// The documents backup planner. Diffs every document version in the instance against one pairing's
/// state rows and fans the difference out into <c>BackupBatch</c> children.
/// </summary>
/// <remarks>
/// <para>
/// <b>This handler does no file I/O.</b> That is what keeps it inside the processor's hard-coded 5-minute
/// per-job timeout on a library of any size: the diff is a pure comparison of stored hashes against
/// stored state rows, so it is bounded by two queries, not by how many gigabytes need copying. Every
/// stream, hash, and write happens in a child.
/// </para>
/// <para>
/// <b>The planner resolves every path and stamps it into the child payload.</b> Children never re-derive
/// one — the same rule the OCR fan-out follows, and load-bearing here: collision suffixes are a function
/// of the whole document set, which a single batch cannot see.
/// </para>
/// </remarks>
public class BackupToTargetJobHandler(
    AppDbContext db,
    IBackupTargetResolver targetResolver,
    IOptions<DocumentBackupEngineOptions> engineOptions,
    IJobSignal signal,
    ILogger<BackupToTargetJobHandler> logger) : IParentJobHandler
{
    /// <summary>
    /// Base priority for batch children. Negative so every user-facing job — an upload, a classify, a
    /// download — preempts the mirror between batches. Claim order is Priority descending, so batch N
    /// sits one step above batch N+1 and the Delete → Move → Put ordering the plan builder relies on
    /// survives into the queue.
    /// </summary>
    public const int ChildBasePriority = -100;

    public ProcessingJobType Type => ProcessingJobType.BackupToTarget;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        // Idempotency: if children already exist (crash recovery), restore TotalChildCount and skip.
        var existingChildCount = await db.ProcessingJobs.CountAsync(j => j.ParentJobId == job.Id, ct);
        if (existingChildCount > 0)
        {
            job.TotalChildCount = existingChildCount;
            return;
        }

        var payload = job.GetPayload<BackupRunPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var sourceId = payload.BackupSourceId;

        // Stamp the pairing onto this job row. The run-now endpoint already does this at enqueue, but a
        // SCHEDULED run cannot — the scheduler copies only the schedule's payload — so a scheduled parent
        // would otherwise be invisible to the single-flight query below while its children drain. The
        // processor saves this for us when the parent handler returns.
        job.EntityType = ProcessingEntityType.BackupSource;
        job.EntityId = sourceId;

        // Tracked, not AsNoTracking: the prune latch below writes to this row.
        var source = await db.BackupSources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);

        // The pairing was deleted while a run sat queued — or, for a job enqueued before this feature
        // existed, never named one at all (Guid.Empty deserializes into sourceId and matches nothing).
        // Neither is a fault: there is nothing left to mirror, so say so and finish clean.
        if (source is null)
        {
            await SkipAsync(job, sourceId, payload.DryRun, "Backup source no longer exists.", ct);
            return;
        }

        // Disabled is an operator decision, not a fault — skip quietly so a still-armed schedule does not
        // fill the job list with failures. Both levels count: pausing the destination has to stop every
        // pairing that writes to it, not just decorate the admin page.
        if (!source.IsEnabled)
        {
            await SkipAsync(job, sourceId, payload.DryRun, "Backup source is disabled.", ct);
            return;
        }

        var targetState = await db.BackupTargetStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TargetKey == source.TargetKey, ct);

        if (targetState is { IsEnabled: false })
        {
            await SkipAsync(job, sourceId, payload.DryRun, "Target is disabled.", ct);
            return;
        }

        var target = targetResolver.Resolve(source.TargetKey);

        // A target that cannot be written to is a configuration error the operator has to fix, so it
        // fails loudly rather than completing quietly — a mirror that reports success while copying
        // nothing is the worst possible outcome for a backup. Checked AFTER the two enable flags: an
        // operator who has already paused this pairing should not be paged about the destination.
        if (!target.IsAvailable)
            throw new BusinessRuleException(
                $"Backup target '{source.TargetKey}' is unavailable: {target.ConfigurationError}");

        // Single-flight, as a plain indexed query on EntityId. Two overlapping planners would each see a
        // pre-change state table and double-enqueue the same copies. The ParentJobId filter is
        // load-bearing: batch children carry the SAME EntityId as their parent, so without it a run would
        // see its own children and every run after the first would skip.
        var alreadyRunning = await db.ProcessingJobs.AnyAsync(j =>
            j.Type == ProcessingJobType.BackupToTarget &&
            j.EntityId == sourceId &&
            j.ParentJobId == null &&
            j.Id != job.Id &&
            (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.InProgress), ct);

        if (alreadyRunning)
        {
            await SkipAsync(job, sourceId, payload.DryRun,
                "A backup run for this source is already in progress.", ct);
            return;
        }

        var plan = await BuildPlanAsync(target, sourceId, payload.AllowLargePrune, ct);

        // A dry run reports the change set without touching anything — that includes the operator-facing
        // latch. The plan's PruneBlocked/BlockedDeleteCount still flow into the summary below so the
        // preview shows what WOULD be withheld; we just don't persist (or clear) the banner for a run
        // that deleted nothing.
        if (!payload.DryRun)
            await ReconcilePruneLatchAsync(source, plan, payload.AllowLargePrune, ct);

        var summary = new BackupRunSummary
        {
            BackupSourceId = sourceId,
            DryRun = payload.DryRun,
            Puts = plan.PutCount,
            Moves = plan.MoveCount,
            Deletes = plan.DeleteCount,
            Unchanged = plan.UnchangedCount,
            BatchCount = payload.DryRun ? 0 : plan.Batches.Count,
            TotalBytes = plan.TotalBytes,
            PruneBlocked = plan.PruneBlocked,
            BlockedDeleteCount = plan.BlockedDeleteCount,
        };

        job.SetResult(summary);

        if (payload.DryRun)
        {
            // Zero children: the processor terminates a childless parent as Completed, so the staged
            // summary is the entire output of the run.
            job.TotalChildCount = 0;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Backup dry run for source {SourceId} ({TargetKey}): {Puts} put(s), {Moves} move(s), " +
                "{Deletes} delete(s), {Unchanged} unchanged", sourceId, source.TargetKey, plan.PutCount,
                plan.MoveCount, plan.DeleteCount, plan.UnchangedCount);
            return;
        }

        for (var index = 0; index < plan.Batches.Count; index++)
        {
            var batch = plan.Batches[index];

            var child = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupBatch,
                Status = ProcessingJobStatus.Pending,
                EntityType = ProcessingEntityType.BackupSource,
                EntityId = sourceId,
                CreatedByUserId = job.CreatedByUserId,
                ParentJobId = job.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Priority = ChildBasePriority - index,
            };
            child.SetPayload(new BackupBatchPayload(sourceId, [.. batch.Actions]));

            db.ProcessingJobs.Add(child);
        }

        job.TotalChildCount = plan.Batches.Count;
        await db.SaveChangesAsync(ct);
        signal.Signal();

        logger.LogInformation(
            "Backup run for source {SourceId} ({TargetKey}) fanned out {BatchCount} batch(es): " +
            "{Puts} put(s), {Moves} move(s), {Deletes} delete(s), {Unchanged} unchanged",
            sourceId, source.TargetKey, plan.Batches.Count, plan.PutCount, plan.MoveCount,
            plan.DeleteCount, plan.UnchangedCount);
    }

    private async Task<BackupPlan> BuildPlanAsync(
        IBackupTarget target, Guid sourceId, bool allowLargePrune, CancellationToken ct)
    {
        // Trashed documents STAY mirrored — trash is a restorable state, and a backup that evaporated the
        // moment a file was moved to trash would be exactly the copy you needed. Only a permanent delete
        // removes the version row, and that is what the anti-join picks up.
        var versions = await db.Documents
            .IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .AsNoTracking()
            .SelectMany(d => d.Versions, (d, v) => new BackupVersionSource(
                v.Id,
                d.Id,
                d.Name,
                d.DocumentDate,
                v.VersionNumber,
                v.FileHash,
                v.FileExtension,
                v.FilePath,
                v.FileSize))
            .ToListAsync(ct);

        var existing = await db.DocumentBackupStates
            .AsNoTracking()
            .Where(s => s.BackupSourceId == sourceId)
            .Select(s => new BackupStateRow(
                s.DocumentVersionId,
                s.DocumentId,
                s.RemoteRelativePath,
                s.RemoteHandle,
                s.BackedUpHash,
                s.FileSize))
            .ToListAsync(ct);

        var desired = BackupPathPlanner.Plan(
            versions, target.Capabilities, DocumentsBackupSourceDescriptor.RootFolderName);

        return BackupPlanBuilder.Build(desired, existing, engineOptions.Value, allowLargePrune);
    }

    // Latches the breaker onto the SOURCE row so the admin page can show "N deletions were blocked" with
    // a Prune anyway action against the pairing that blocked them, and clears it again once that action
    // has been taken. Clearing is an explicit admin act — PATCH with acknowledgePruneBlock, or THIS run
    // being the authorised override. A run that simply had fewer deletions must NOT un-latch it, or the
    // warning would disappear before anyone saw it, which is why the clear is gated on allowLargePrune
    // rather than on "the plan came back unblocked".
    private async Task ReconcilePruneLatchAsync(
        BackupSource source, BackupPlan plan, bool allowLargePrune, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (plan.PruneBlocked)
        {
            source.PruneBlockedAt = now;
            source.PruneBlockedCount = plan.BlockedDeleteCount;
            source.UpdatedAt = now;

            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Backup source {SourceId} ({TargetKey}) withheld {Count} deletion(s): the bulk-delete " +
                "circuit breaker tripped. Puts and moves still ran. Acknowledge the latch or re-run with " +
                "allowLargePrune to prune.", source.Id, source.TargetKey, plan.BlockedDeleteCount);

            return;
        }

        // The override ran: the deletions the latch was withholding have now been planned and handed to
        // the children, so leaving the banner up would report a block that no longer exists — a stale
        // "N deletions were blocked on <old date>" that the admin can only silence with a second, now
        // meaningless, Dismiss.
        if (!allowLargePrune || source.PruneBlockedAt is null)
            return;

        source.PruneBlockedAt = null;
        source.PruneBlockedCount = null;
        source.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Backup source {SourceId} ({TargetKey}) cleared its bulk-delete latch: the run was authorised " +
            "to prune and planned {Count} deletion(s).", source.Id, source.TargetKey, plan.DeleteCount);
    }

    private async Task SkipAsync(
        ProcessingJob job, Guid sourceId, bool dryRun, string reason, CancellationToken ct)
    {
        job.SetResult(new BackupRunSummary
        {
            BackupSourceId = sourceId,
            DryRun = dryRun,
            Skipped = true,
            SkipReason = reason,
        });

        job.TotalChildCount = 0;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Backup run for source {SourceId} skipped: {Reason}", sourceId, reason);
    }
}
