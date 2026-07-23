using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
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
/// The backup planner. Diffs every document version in the instance against one target's state rows and
/// fans the difference out into <c>BackupBatch</c> children.
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
    IOptions<BackupOptions> backupOptions,
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

        var payload = job.GetPayload<BackupToTargetPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var targetKey = payload.TargetKey;
        var entityId = DeterministicGuid.FromKey(targetKey);

        // Stamp the target onto this job row. The run-now endpoint already does this at enqueue, but a
        // SCHEDULED run cannot — the scheduler copies only the schedule's payload — so a scheduled parent
        // would otherwise be invisible to the single-flight query below while its children drain. The
        // processor saves this for us when the parent handler returns.
        job.EntityType = ProcessingEntityType.BackupTarget;
        job.EntityId = entityId;

        var target = targetResolver.Resolve(targetKey);

        // A target that cannot be written to is a configuration error the operator has to fix, so it
        // fails loudly rather than completing quietly — a mirror that reports success while copying
        // nothing is the worst possible outcome for a backup.
        if (!target.IsAvailable)
            throw new BusinessRuleException(
                $"Backup target '{targetKey}' is unavailable: {target.ConfigurationError}");

        var state = await db.BackupTargetStates
            .FirstOrDefaultAsync(s => s.TargetKey == targetKey, ct);

        // Disabled is an operator decision, not a fault — skip quietly so a still-armed schedule does not
        // fill the job list with failures.
        if (state is { IsEnabled: false })
        {
            await SkipAsync(job, targetKey, payload.DryRun, "Target is disabled.", ct);
            return;
        }

        // Single-flight, as a plain indexed query on EntityId. Two overlapping planners would each see a
        // pre-change state table and double-enqueue the same copies.
        var alreadyRunning = await db.ProcessingJobs.AnyAsync(j =>
            j.Type == ProcessingJobType.BackupToTarget &&
            j.EntityId == entityId &&
            j.Id != job.Id &&
            (j.Status == ProcessingJobStatus.Pending || j.Status == ProcessingJobStatus.InProgress), ct);

        if (alreadyRunning)
        {
            await SkipAsync(job, targetKey, payload.DryRun,
                "A backup run for this target is already in progress.", ct);
            return;
        }

        var plan = await BuildPlanAsync(target, targetKey, payload.AllowLargePrune, ct);

        // A dry run reports the change set without touching anything — that includes the operator-facing
        // latch. The plan's PruneBlocked/BlockedDeleteCount still flow into the summary below so the
        // preview shows what WOULD be withheld; we just don't persist the banner or a state row for a run
        // that deleted nothing.
        if (!payload.DryRun)
            await ApplyPruneLatchAsync(state, targetKey, plan, ct);

        var summary = new BackupRunSummary
        {
            TargetKey = targetKey,
            DryRun = payload.DryRun,
            PutCount = plan.PutCount,
            MoveCount = plan.MoveCount,
            DeleteCount = plan.DeleteCount,
            UnchangedCount = plan.UnchangedCount,
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
                "Backup dry run for target {TargetKey}: {Puts} put(s), {Moves} move(s), {Deletes} delete(s), " +
                "{Unchanged} unchanged", targetKey, plan.PutCount, plan.MoveCount, plan.DeleteCount,
                plan.UnchangedCount);
            return;
        }

        for (var index = 0; index < plan.Batches.Count; index++)
        {
            var batch = plan.Batches[index];

            var job2 = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupBatch,
                Status = ProcessingJobStatus.Pending,
                EntityType = ProcessingEntityType.BackupTarget,
                EntityId = entityId,
                CreatedByUserId = job.CreatedByUserId,
                ParentJobId = job.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Priority = ChildBasePriority - index,
            };
            job2.SetPayload(new BackupBatchPayload(targetKey, [.. batch.Actions]));

            db.ProcessingJobs.Add(job2);
        }

        job.TotalChildCount = plan.Batches.Count;
        await db.SaveChangesAsync(ct);
        signal.Signal();

        logger.LogInformation(
            "Backup run for target {TargetKey} fanned out {BatchCount} batch(es): {Puts} put(s), " +
            "{Moves} move(s), {Deletes} delete(s), {Unchanged} unchanged",
            targetKey, plan.Batches.Count, plan.PutCount, plan.MoveCount, plan.DeleteCount,
            plan.UnchangedCount);
    }

    private async Task<BackupPlan> BuildPlanAsync(
        IBackupTarget target, string targetKey, bool allowLargePrune, CancellationToken ct)
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
            .Where(s => s.TargetKey == targetKey)
            .Select(s => new BackupStateRow(
                s.DocumentVersionId,
                s.DocumentId,
                s.RemoteRelativePath,
                s.RemoteHandle,
                s.BackedUpHash,
                s.FileSize))
            .ToListAsync(ct);

        var desired = BackupPathPlanner.Plan(versions, target.Capabilities);

        return BackupPlanBuilder.Build(desired, existing, backupOptions.Value, allowLargePrune);
    }

    // Latches the breaker onto the target's state row so the admin page can show "N deletions were
    // blocked" with a Prune anyway action. Clearing it is an explicit admin act (PATCH, or a run with
    // allowLargePrune) — a run that simply had fewer deletions must not silently un-latch it, or the
    // warning would disappear before anyone saw it.
    private async Task ApplyPruneLatchAsync(
        BackupTargetState? state, string targetKey, BackupPlan plan, CancellationToken ct)
    {
        if (!plan.PruneBlocked)
            return;

        var now = DateTimeOffset.UtcNow;

        if (state is null)
        {
            state = new BackupTargetState
            {
                TargetKey = targetKey,
                IsEnabled = true,
                CreatedAt = now,
            };
            db.BackupTargetStates.Add(state);
        }

        state.PruneBlockedAt = now;
        state.PruneBlockedCount = plan.BlockedDeleteCount;
        state.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Backup target {TargetKey} withheld {Count} deletion(s): the bulk-delete circuit breaker " +
            "tripped. Puts and moves still ran. Clear the latch or re-run with allowLargePrune to prune.",
            targetKey, plan.BlockedDeleteCount);
    }

    private async Task SkipAsync(
        ProcessingJob job, string targetKey, bool dryRun, string reason, CancellationToken ct)
    {
        job.SetResult(new BackupRunSummary
        {
            TargetKey = targetKey,
            DryRun = dryRun,
            Skipped = true,
            SkipReason = reason,
        });

        job.TotalChildCount = 0;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Backup run for target {TargetKey} skipped: {Reason}", targetKey, reason);
    }
}
