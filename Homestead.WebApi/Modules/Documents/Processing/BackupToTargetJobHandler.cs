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
    IBackupTargetGate gate,
    IOptions<DocumentBackupEngineOptions> engineOptions,
    IJobSignal signal,
    ILogger<BackupToTargetJobHandler> logger) : IParentJobHandler
{
    /// <summary>
    /// Base priority for batch children. Negative so every user-facing job — an upload, a classify, a
    /// download — preempts the mirror between batches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A child's priority is <c>ChildBasePriority - (int)batch.Stage</c>, so priority ENCODES the pipeline
    /// stage. Among one parent's children the processor treats a strictly higher priority as a
    /// happens-before edge — it will not claim a job while a higher-priority sibling is still Pending or
    /// InProgress, and a failed job cancels its lower-priority siblings outright. Batches sharing a stage
    /// share a priority and so stay fully concurrent; the Delete → Move → Put → Prune ordering the plan
    /// builder relies on becomes a real ordering guarantee rather than a claim-order hint. Gaps in the
    /// sequence when a stage is empty are harmless.
    /// </para>
    /// <para>
    /// <b>Warning for future fan-out handlers:</b> giving children DIFFERING priorities is now opting into
    /// serialization between those priority levels. A handler that only wants "do this one first if there
    /// is a free worker" should leave every child at the same priority — the flat fan-outs (bulk classify,
    /// bulk reprocess) all do.
    /// </para>
    /// </remarks>
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
        var verdict = await gate.CheckAsync(target, ct);
        if (!verdict.IsAvailable)
            throw new BusinessRuleException(
                $"Backup target '{source.TargetKey}' is unavailable: {verdict.Reason}");

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

        // Tagged with the lowest stage, so it takes the lowest child priority and therefore runs — not
        // merely claims — after every mutation batch in the run, including any continuation, which
        // inherits its predecessor's priority and so still sits above this. That ordering is the whole
        // reason the prune is a batch rather than an inline step, and the queue now enforces it.
        var batches = payload.DryRun
            ? plan.Batches
            : AppendPruneBatch(plan, target);

        var summary = new BackupRunSummary
        {
            BackupSourceId = sourceId,
            DryRun = payload.DryRun,
            Puts = plan.PutCount,
            Moves = plan.MoveCount,
            Deletes = plan.DeleteCount,
            Unchanged = plan.UnchangedCount,
            BatchCount = payload.DryRun ? 0 : batches.Count,
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

        // The one stated exception to "the planner does no I/O". An id-addressed destination cannot let
        // batches find-or-create folders concurrently — that race mints twin year-shards and splits the
        // mirror permanently — so folders are resolved once, here, sequentially. Bounded by the number of
        // NEW folders (roughly, new year shards — tens), never by file count, so it cannot threaten the
        // job timeout this rule exists to protect. A no-op for a filesystem target.
        await target.EnsureFoldersAsync(PlannedFolders(batches), ct);

        foreach (var batch in batches)
        {
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
                Priority = ChildBasePriority - (int)batch.Stage,
            };
            child.SetPayload(new BackupBatchPayload(sourceId, [.. batch.Actions]));

            db.ProcessingJobs.Add(child);
        }

        job.TotalChildCount = batches.Count;
        await db.SaveChangesAsync(ct);
        signal.Signal();

        logger.LogInformation(
            "Backup run for source {SourceId} ({TargetKey}) fanned out {BatchCount} batch(es): " +
            "{Puts} put(s), {Moves} move(s), {Deletes} delete(s), {Unchanged} unchanged",
            sourceId, source.TargetKey, batches.Count, plan.PutCount, plan.MoveCount,
            plan.DeleteCount, plan.UnchangedCount);
    }

    /// <summary>
    /// The plan's batches, plus a final trash-only folder sweep when the destination needs one.
    /// </summary>
    /// <remarks>
    /// Candidates are the folders this run is about to empty — the parents of every delete and of every
    /// move's ORIGIN. A move's destination is not a candidate: something just landed in it.
    /// </remarks>
    private static IReadOnlyList<BackupActionBatch> AppendPruneBatch(BackupPlan plan, IBackupTarget target)
    {
        if (!target.Capabilities.DefersFolderPrune)
            return plan.Batches;

        var candidates = plan.Batches
            .SelectMany(b => b.Actions)
            .Select(a => a.Kind switch
            {
                BackupActionKind.Delete => ParentFolder(a.RelativePath),
                BackupActionKind.Move => ParentFolder(a.FromRelativePath ?? string.Empty),
                _ => null
            })
            .OfType<string>()
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            // Deepest first, so trashing a leaf makes its parent a candidate within the same sweep.
            .OrderByDescending(p => p.Count(c => c == '/'))
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p => new BackupAction { Kind = BackupActionKind.PruneFolder, RelativePath = p })
            .ToList();

        if (candidates.Count == 0)
            return plan.Batches;

        return [.. plan.Batches, new BackupActionBatch(BackupStage.Prune, candidates)];
    }

    /// <summary>Every folder a batch is about to write into — what the ensure step has to guarantee.</summary>
    private static IReadOnlyCollection<string> PlannedFolders(IReadOnlyList<BackupActionBatch> batches) =>
        batches
            .SelectMany(b => b.Actions)
            .Where(a => a.Kind is BackupActionKind.Put or BackupActionKind.Move)
            .Select(a => ParentFolder(a.RelativePath))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string ParentFolder(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? string.Empty : relativePath[..index];
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

        var options = engineOptions.Value;

        // The documents engine's caps, then the destination's override on top. A Drive target says
        // "25 files / 64 MB" here where local disk says "100 / 512 MB", because the binding constraint
        // stopped being disk bandwidth and became the household's uplink.
        var caps = BackupBatchCaps.Resolve(
            options.MaxFilesPerBatch,
            options.MaxBytesPerBatch,
            options.MaxOpsPerMetadataBatch,
            target.BatchCapOverrides);

        return BackupPlanBuilder.Build(desired, existing, options, caps, allowLargePrune);
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
