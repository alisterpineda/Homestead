using System.Security.Cryptography;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Processing;

/// <summary>
/// Executes one slice of a backup plan: copies, moves, and deletes objects on the target, then records
/// what landed. Every path in the payload was resolved by the parent — this handler never re-derives one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The job timeout is a pacing device here, not a failure mode.</b> Against a residential uplink it is
/// the binding constraint on everything, so the batch stops starting new work with a safety margin left,
/// persists what finished, and enqueues a continuation under the same parent before completing
/// <i>successfully</i>. A large change set converges over a chain of green batches instead of a serial
/// procession of Failed runs that make a working backup read as broken.
/// </para>
/// </remarks>
public class BackupBatchJobHandler(
    AppDbContext db,
    IBackupTargetResolver targetResolver,
    IBackupTargetGate gate,
    IOptions<FileStorageOptions> storage,
    IOptions<JobProcessingOptions> processingOptions,
    IProcessingNotifier notifier,
    IJobSignal signal,
    TimeProvider timeProvider,
    ILogger<BackupBatchJobHandler> logger) : IJobHandler
{
    /// <summary>
    /// How many completed actions to accumulate before flushing state rows. A crash between a target
    /// write and its state row is harmless — the path is stable, so the next run simply re-copies — but
    /// losing a whole batch's worth of progress to one timeout would stall a mirror that can never quite
    /// finish. Small enough to bound that, large enough to avoid a round trip per file.
    /// </summary>
    private const int StateFlushInterval = 25;

    /// <summary>
    /// Slack subtracted from the processor's own timeout. Everything after the last action — the final
    /// flush, the prune dispatch, the continuation insert — has to fit inside this, and being killed
    /// there would lose exactly the bookkeeping that makes the next batch cheap.
    /// </summary>
    private static readonly TimeSpan DeadlineMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Below this much remaining budget, starting another action is a bad bet: it will be cut off
    /// mid-flight and (for anything that is not resumable) redone from scratch next time.
    /// </summary>
    private static readonly TimeSpan MinimumActionBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a parked upload session is worth resuming. Comfortably inside Drive's own ~one-week
    /// window, because a session the destination has already forgotten fails every retry identically —
    /// which for one file is an unmirrorable file, forever.
    /// </summary>
    private static readonly TimeSpan MaxSessionAge = TimeSpan.FromDays(5);

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public ProcessingJobType Type => ProcessingJobType.BackupBatch;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<BackupBatchPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        // Backstop for the enqueue-vs-delete race: the pairing can be deleted (taking its state rows with
        // it) while its batches sit queued. Writing state for a source that no longer exists would
        // violate the FK and fail the batch loudly for something that is not a fault, so re-check and
        // return quietly instead. Deliberately AsNoTracking — nothing here writes to the source row.
        var source = await db.BackupSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == payload.BackupSourceId, ct);

        if (source is null)
        {
            logger.LogInformation(
                "Backup batch for source {SourceId} skipped: the backup source was deleted after this " +
                "batch was queued", payload.BackupSourceId);
            return;
        }

        var target = targetResolver.Resolve(source.TargetKey);

        // The composed check, not target.IsAvailable: a credential that died mid-run has already latched
        // on the target row, and this is what lets every still-queued sibling batch fail fast off that
        // marker instead of hammering the token endpoint in turn.
        var verdict = await gate.CheckAsync(target, ct);
        if (!verdict.IsAvailable)
            throw new InvalidOperationException(
                $"Backup target '{source.TargetKey}' is unavailable: {verdict.Reason}");

        var mediaPath = storage.Value.MediaPath;
        var deadline = ResolveDeadline(job);

        var versionIds = payload.Actions.Select(a => a.VersionId).ToList();

        var stateByVersion = await db.DocumentBackupStates
            .Where(s => s.BackupSourceId == payload.BackupSourceId
                        && versionIds.Contains(s.DocumentVersionId))
            .ToDictionaryAsync(s => s.DocumentVersionId, ct);

        var sessionByVersion = await db.BackupUploadSessions
            .Where(s => s.BackupSourceId == payload.BackupSourceId
                        && versionIds.Contains(s.DocumentVersionId))
            .ToDictionaryAsync(s => s.DocumentVersionId, ct);

        var failures = new List<BackupActionFailure>();
        var pruneCandidates = new List<string>();
        var deferred = new List<BackupAction>();
        var pendingSinceFlush = 0;
        var skippedPuts = 0;
        var outOfSpace = false;
        string? outOfSpaceMessage = null;

        for (var index = 0; index < payload.Actions.Count; index++)
        {
            var action = payload.Actions[index];

            // Folder pruning is best-effort housekeeping with no state row; batched up and dispatched in
            // one call at the end so the target can walk each tree once.
            if (action.Kind == BackupActionKind.PruneFolder)
            {
                pruneCandidates.Add(action.RelativePath);
                continue;
            }

            // Out of space stops copies dead — they cannot succeed — but moves and deletes still run,
            // because deletes are exactly what frees space.
            if (outOfSpace && action.Kind == BackupActionKind.Put)
            {
                skippedPuts++;
                continue;
            }

            if (IsOutOfBudget(deadline))
            {
                // Everything from here, including this action, becomes the continuation's problem.
                deferred.AddRange(payload.Actions.Skip(index));
                break;
            }

            try
            {
                var outcome = action.Kind switch
                {
                    BackupActionKind.Put => await PutAsync(
                        target, payload.BackupSourceId, mediaPath, action, deadline,
                        stateByVersion, sessionByVersion, ct),
                    BackupActionKind.Move => await MoveAsync(target, action, stateByVersion, ct),
                    BackupActionKind.Delete => await DeleteAsync(target, action, stateByVersion, ct),
                    _ => throw new InvalidOperationException($"Unknown backup action '{action.Kind}'.")
                };

                if (outcome is ActionOutcome.Suspended)
                    deferred.Add(action);

                if (outcome is not ActionOutcome.Recorded)
                    continue;

                if (++pendingSinceFlush >= StateFlushInterval)
                {
                    await db.SaveChangesAsync(ct);
                    pendingSinceFlush = 0;
                }
            }
            catch (BackupTargetOutOfSpaceException ex)
            {
                // Not counted as a failure per file: two hundred identical "out of space" fragments say
                // nothing the one rolled-up message does not. The batch still fails, once, at the end,
                // carrying the destination's own wording rather than a copy of it kept here.
                outOfSpace = true;
                outOfSpaceMessage ??= ex.Message;

                // Only a COPY is "skipped" by a full destination. A move or delete that hit the same wall
                // genuinely failed, and counting it as a skipped copy would misreport the run.
                if (action.Kind == BackupActionKind.Put)
                {
                    skippedPuts++;
                }
                else
                {
                    logger.LogWarning(
                        "Backup {Kind} for '{Path}' on target {TargetKey} hit the destination's storage " +
                        "limit", action.Kind, action.RelativePath, source.TargetKey);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or shutdown — let the processor own it. Persist what already succeeded first so
                // the next run doesn't redo it.
                await SaveQuietlyAsync();
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Backup {Kind} failed for version {VersionId} on target {TargetKey} at {Path}",
                    action.Kind, action.VersionId, source.TargetKey, action.RelativePath);

                failures.Add(new BackupActionFailure(
                    action.VersionId, action.Kind.ToString(), action.RelativePath, ex.Message));
            }
        }

        // Persist everything that succeeded BEFORE reporting the failures. One bad file must not cost the
        // other ninety-nine their state rows — that is what turns a transient error into a permanent
        // re-copy loop.
        await db.SaveChangesAsync(ct);

        // The continuation carries real work and goes first: pruning is cosmetic housekeeping with no
        // budget check of its own, so letting it run ahead would risk the processor's timeout landing
        // between the two and silently dropping everything the continuation was going to carry.
        if (deferred.Count > 0)
            await EnqueueContinuationAsync(job, payload.BackupSourceId, deferred);

        if (pruneCandidates.Count > 0)
            await PruneAsync(target, pruneCandidates, source.TargetKey, deadline, ct);

        if (job.ParentJobId is null)
        {
            await notifier.NotifyJobStatusAsync(
                job.Id, ProcessingJobStatus.Completed,
                ProcessingEntityType.BackupSource, job.EntityId,
                documentName: $"Backup batch ({payload.Actions.Count} action(s))",
                error: null, job.CreatedByUserId, ct);
        }

        // One clean message, and a typed marker beside it. Nothing latches: quota frees up entirely
        // outside Homestead (the user empties their trash, buys storage) with no event to observe, so the
        // next scheduled run simply retries. The per-action failures ride along: a move that genuinely
        // failed in the same batch is not erased by the destination also being full.
        if (outOfSpace)
        {
            job.SetResult(BuildOutcome(BackupBatchOutcome.OutOfSpaceCode, skippedPuts, failures));
            await db.SaveChangesAsync(ct);

            throw new InvalidOperationException(outOfSpaceMessage ?? OutOfSpaceMessage);
        }

        // Throwing here fails this batch, which surfaces at the parent as PartiallyCompleted rather than
        // as a silent success — the admin needs to see that some files did not make it.
        if (failures.Count > 0)
        {
            // Staged BEFORE the throw, and after the flush above that persisted the successes. The thrown
            // message is capped and joined, which makes it useless for answering "did THIS file fail" —
            // the typed list is what a per-document reader joins on, by version id.
            job.SetResult(BuildOutcome(errorCode: null, skippedPuts, failures));
            await db.SaveChangesAsync(ct);

            throw new InvalidOperationException(DescribeFailures(failures, payload.Actions.Count));
        }
    }

    /// <summary>
    /// How many per-action failures are staged on the job. The count on the outcome stays whole, so a cap
    /// here reads as "not listed" rather than as "did not fail".
    /// </summary>
    private const int MaxStagedFailures = 100;

    /// <summary>How many failures the thrown message names before rolling the rest into a count.</summary>
    private const int MaxDescribedFailures = 10;

    private static BackupBatchOutcome BuildOutcome(
        string? errorCode, int skippedPuts, List<BackupActionFailure> failures) =>
        new(errorCode,
            skippedPuts,
            failures.Count == 0 ? null : failures.Take(MaxStagedFailures).ToList(),
            failures.Count);

    /// <summary>
    /// The batch's failure message, composed from the same list that is staged on the job so the two can
    /// never describe different things.
    /// </summary>
    private static string DescribeFailures(List<BackupActionFailure> failures, int actionCount) =>
        $"{failures.Count} of {actionCount} backup action(s) failed: " +
        string.Join("; ", failures
            .Take(MaxDescribedFailures)
            .Select(f => $"{f.Kind} '{f.RelativePath}': {f.Message}")) +
        (failures.Count > MaxDescribedFailures
            ? $" (+{failures.Count - MaxDescribedFailures} more)"
            : string.Empty);

    /// <summary>
    /// Fallback wording only. The destination's own exception message wins where there is one — this
    /// handler mirrors whatever a source kind is paired with and has no business naming a vendor.
    /// </summary>
    private const string OutOfSpaceMessage = "The backup target is out of storage space.";

    /// <summary>What one action did, as far as the loop is concerned.</summary>
    private enum ActionOutcome
    {
        /// <summary>Nothing happened; nothing to flush.</summary>
        Skipped,

        /// <summary>State was written or removed — counts toward the flush interval.</summary>
        Recorded,

        /// <summary>Parked mid-upload. No state row, and the action goes to the continuation.</summary>
        Suspended
    }

    /// <summary>
    /// When this batch must stop starting work. Derived from the processor's own timeout so the two can
    /// never drift apart.
    /// </summary>
    /// <remarks>
    /// Anchored on <c>StartedAt</c> — the claim instant — rather than on handler entry, so the budget is
    /// conservative: any delay between the claim and this handler running comes out of our margin rather
    /// than being handed back to us. <c>StartedAt</c> is re-stamped by every claim, so a batch reclaimed
    /// after a crash gets a fresh budget rather than inheriting a spent one. Note the two clocks:
    /// <c>StartedAt</c> is stamped by the database's <c>now()</c> while the budget is read from the injected
    /// provider. They agree in production to within the host/DB clock skew; a test that pins the provider
    /// only ever inflates the budget (the pin is taken from the current instant and then falls behind),
    /// which is the safe direction.
    /// </remarks>
    private DateTimeOffset ResolveDeadline(ProcessingJob job) =>
        (job.StartedAt ?? timeProvider.GetUtcNow())
        + processingOptions.Value.ResolveJobTimeout()
        - DeadlineMargin;

    private bool IsOutOfBudget(DateTimeOffset deadline) =>
        deadline - timeProvider.GetUtcNow() < MinimumActionBudget;

    /// <summary>Copies a blob to the target and records it.</summary>
    private async Task<ActionOutcome> PutAsync(
        IBackupTarget target,
        Guid backupSourceId,
        string mediaPath,
        BackupAction action,
        DateTimeOffset deadline,
        Dictionary<Guid, DocumentBackupState> stateByVersion,
        Dictionary<Guid, BackupUploadSession> sessionByVersion,
        CancellationToken ct)
    {
        var sourcePath = Path.Combine(mediaPath, action.SourceFilePath ?? string.Empty);

        if (string.IsNullOrEmpty(action.SourceFilePath) || !File.Exists(sourcePath))
        {
            // Two very different situations look identical here, and conflating them is what would let a
            // backup quietly under-copy forever:
            //
            //   • The version row is GONE — permanently deleted between the parent's plan and this batch.
            //     An expected race. Skip it; the next run's anti-join tidies the mirror, and because the
            //     version no longer exists it is never re-planned.
            //
            //   • The version row is STILL THERE but its blob is not — the library itself has lost a file.
            //     Skipping that silently means no state row is written, so the copy is re-planned on
            //     EVERY subsequent run: the mirror can never reach a steady state, and the run still
            //     reports Completed with a copy count that never happened. Fail the action instead, so it
            //     surfaces as PartiallyCompleted with the version named.
            var versionStillExists = await db.DocumentVersions
                .AsNoTracking()
                .AnyAsync(v => v.Id == action.VersionId, ct);

            if (versionStillExists)
            {
                throw new FileNotFoundException(
                    $"Source blob for version {action.VersionId} is missing from storage at " +
                    $"'{action.SourceFilePath}'. The document still exists, so this is lost data rather " +
                    "than a deletion race.",
                    sourcePath);
            }

            logger.LogWarning(
                "Backup source for version {VersionId} is gone (the version was deleted mid-run), " +
                "skipping: {SourcePath}", action.VersionId, sourcePath);

            // A parked session for a version that no longer exists will never be resumed by anything;
            // left alone the row outlives its subject and is only ever reaped by the pairing's cascade.
            if (sessionByVersion.Remove(action.VersionId, out var abandoned))
                db.BackupUploadSessions.Remove(abandoned);

            return ActionOutcome.Skipped;
        }

        // Verify BEFORE writing, which costs a second pass over the file but is the whole point: the
        // mirror is the copy of last resort, so propagating bytes that no longer match the recorded hash
        // would quietly replace a good backup with a corrupt one. The read is streamed, never buffered.
        // MD5 rides along in the same pass, so a destination whose own checksum is documented "if
        // available" still has something to compare against without a second read.
        var (actualHash, md5Hex) = await ComputeHashesAsync(
            sourcePath, alsoMd5: target.Capabilities.SupportsContentHash, ct);

        if (!string.Equals(actualHash, action.FileHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Content hash mismatch for version {action.VersionId} at '{action.SourceFilePath}': " +
                $"expected {action.FileHash}, found {actualHash}. Refusing to copy corrupt content.");
        }

        var session = ResolveSession(action, sessionByVersion);

        var context = new BackupPutContext(
            ContentType: ResolveContentType(action.RelativePath),
            Sha256Hex: action.FileHash,
            Md5Hex: md5Hex,
            ResumeSessionUri: session?.SessionUri,
            ResumeOffset: session?.BytesCommitted ?? 0,
            Deadline: deadline);

        BackupPutResult result;
        await using (var content = File.OpenRead(sourcePath))
        {
            result = await target.PutAsync(
                new BackupObjectRef(action.RelativePath, action.Handle), content, context, ct);
        }

        if (result is BackupPutResult.Suspended suspended)
        {
            UpsertSession(backupSourceId, action, suspended, sessionByVersion);
            return ActionOutcome.Suspended;
        }

        var handle = ((BackupPutResult.Completed)result).Handle;

        // The upload finished, so a parked session for it is spent.
        if (sessionByVersion.Remove(action.VersionId, out var finished))
            db.BackupUploadSessions.Remove(finished);

        // Only now — after the write returned — does the state row claim the copy exists.
        Upsert(backupSourceId, action, handle, stateByVersion);
        return ActionOutcome.Recorded;
    }

    /// <summary>
    /// Returns the parked session to resume, discarding one whose content no longer matches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hash check is what stops two different files being spliced together: a version id can be
    /// re-pointed at new bytes, and resuming the old session would commit the tail of the new content on
    /// top of the head of the old.
    /// </para>
    /// <para>
    /// The age check is what stops a permanent stall. A destination's resumable session has a lifetime of
    /// its own (about a week on Drive), and an expired URI answers every resume with a flat failure — so
    /// a session kept past its shelf life would be retried on every run forever and that file would never
    /// be mirrored again. Expiring it locally, well inside the destination's window, turns that into one
    /// re-upload from byte zero.
    /// </para>
    /// </remarks>
    private BackupUploadSession? ResolveSession(
        BackupAction action, Dictionary<Guid, BackupUploadSession> sessionByVersion)
    {
        if (!sessionByVersion.TryGetValue(action.VersionId, out var session))
            return null;

        var reason =
            !string.Equals(session.FileHash, action.FileHash, StringComparison.OrdinalIgnoreCase)
                ? "its content changed"
                : timeProvider.GetUtcNow() - session.CreatedAt > MaxSessionAge
                    ? "it is older than the destination will keep it"
                    : null;

        if (reason is null)
            return session;

        logger.LogInformation(
            "Discarding the parked upload session for version {VersionId}: {Reason}",
            action.VersionId, reason);

        db.BackupUploadSessions.Remove(session);
        sessionByVersion.Remove(action.VersionId);
        return null;
    }

    private void UpsertSession(
        Guid backupSourceId,
        BackupAction action,
        BackupPutResult.Suspended suspended,
        Dictionary<Guid, BackupUploadSession> sessionByVersion)
    {
        // An empty session URI means the budget expired before the destination even issued one. There is
        // nothing to resume and nothing was written — the action simply re-runs in the continuation.
        if (string.IsNullOrEmpty(suspended.SessionUri))
            return;

        var now = timeProvider.GetUtcNow();

        if (sessionByVersion.TryGetValue(action.VersionId, out var existing))
        {
            existing.SessionUri = suspended.SessionUri;
            existing.BytesCommitted = suspended.BytesCommitted;
            existing.FileHash = action.FileHash;
            existing.UpdatedAt = now;
            return;
        }

        var session = new BackupUploadSession
        {
            BackupSourceId = backupSourceId,
            DocumentVersionId = action.VersionId,
            SessionUri = suspended.SessionUri,
            BytesCommitted = suspended.BytesCommitted,
            FileHash = action.FileHash,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.BackupUploadSessions.Add(session);
        sessionByVersion[action.VersionId] = session;
    }

    private async Task<ActionOutcome> MoveAsync(
        IBackupTarget target,
        BackupAction action,
        Dictionary<Guid, DocumentBackupState> stateByVersion,
        CancellationToken ct)
    {
        if (!stateByVersion.TryGetValue(action.VersionId, out var state))
        {
            logger.LogWarning(
                "Backup move for version {VersionId} has no state row, skipping", action.VersionId);
            return ActionOutcome.Skipped;
        }

        try
        {
            var handle = await target.MoveAsync(
                new BackupObjectRef(action.FromRelativePath ?? state.RemoteRelativePath, state.RemoteHandle),
                action.RelativePath,
                ct);

            state.RemoteRelativePath = action.RelativePath;
            state.RemoteHandle = handle;
            state.BackedUpAt = timeProvider.GetUtcNow();
        }
        catch (FileNotFoundException)
        {
            // The object vanished from the mirror (removed out of band, or a previous run died between
            // the write and its state row). Drop the row so the next run's diff sees "no state" and
            // re-copies from scratch — self-healing, and cheaper than failing the batch.
            logger.LogWarning(
                "Backup move source missing for version {VersionId} at {Path}; dropping state row so the " +
                "next run re-copies it", action.VersionId, action.FromRelativePath);

            db.DocumentBackupStates.Remove(state);
            stateByVersion.Remove(action.VersionId);
        }

        return ActionOutcome.Recorded;
    }

    private async Task<ActionOutcome> DeleteAsync(
        IBackupTarget target,
        BackupAction action,
        Dictionary<Guid, DocumentBackupState> stateByVersion,
        CancellationToken ct)
    {
        await target.DeleteAsync(new BackupObjectRef(action.RelativePath, action.Handle), ct);

        if (stateByVersion.TryGetValue(action.VersionId, out var state))
        {
            db.DocumentBackupStates.Remove(state);
            stateByVersion.Remove(action.VersionId);
        }

        return ActionOutcome.Recorded;
    }

    private async Task PruneAsync(
        IBackupTarget target,
        List<string> folderPaths,
        string targetKey,
        DateTimeOffset deadline,
        CancellationToken ct)
    {
        // Housekeeping is never worth being killed for. On an id-addressed destination each candidate
        // costs a list plus possibly an update, which does not reliably fit in the leftover margin —
        // and a folder left standing is cosmetic where a killed job is a red run.
        if (timeProvider.GetUtcNow() >= deadline)
        {
            logger.LogInformation(
                "Skipping the folder prune on backup target {TargetKey}: the batch is out of budget",
                targetKey);
            return;
        }

        try
        {
            await target.PruneEmptyFoldersAsync(folderPaths, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never added to `failures`: an empty folder left behind is cosmetic, and failing the run's
            // last batch over housekeeping would paint a fully-converged mirror red.
            logger.LogWarning(ex,
                "Best-effort folder prune failed on backup target {TargetKey}", targetKey);
        }
    }

    /// <summary>
    /// Enqueues the remaining actions as a sibling batch under the same parent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is load-bearing. <c>TotalChildCount</c> is bumped and the child inserted inside one
    /// transaction, so the generic parent-progress logic can never observe a new child without having
    /// counted it — which would terminate the parent early, while this batch is still running. The
    /// conditional bump doubles as the guard: if the parent is no longer <c>InProgress</c> (cancelled, or
    /// already terminal) the update matches zero rows and no orphan child is inserted.
    /// </para>
    /// <para>
    /// The continuation carries the <b>same priority</b> as its predecessor rather than a lower one, which
    /// under the sibling happens-before rule is exactly right: priority encodes the pipeline STAGE, and a
    /// continuation belongs to the same stage as the batch it came from. Equal priority imposes no edge,
    /// so it stays non-blocking against its own stage's other batches — while still blocking every later
    /// stage, including the run's final prune batch, which sits at the lowest priority of all. A LOWER
    /// priority would be wrong twice over: it would claim to be a later stage, and it would make the
    /// continuation cancellable by an unrelated failure in a peer batch of its own stage.
    /// </para>
    /// <para>
    /// An explicit transaction is legal here because a job scope is outside the command pipeline: the
    /// transaction interceptors are scope-gated off, and EF execution-strategy retries are disabled.
    /// </para>
    /// </remarks>
    private async Task EnqueueContinuationAsync(
        ProcessingJob job, Guid backupSourceId, List<BackupAction> remaining)
    {
        if (job.ParentJobId is not { } parentId)
        {
            // A standalone batch has no parent to account for, so there is nothing to keep atomic.
            await EnqueueStandaloneContinuationAsync(job, backupSourceId, remaining);
            return;
        }

        // CancellationToken.None throughout: this runs after the last action, inside the safety margin,
        // and losing it would silently drop the remaining work rather than deferring it.
        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);

        var bumped = await db.ProcessingJobs
            .Where(j => j.Id == parentId && j.Status == ProcessingJobStatus.InProgress)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.TotalChildCount, j => j.TotalChildCount + 1),
                CancellationToken.None);

        if (bumped == 1)
        {
            db.ProcessingJobs.Add(BuildContinuation(job, backupSourceId, remaining, job.Priority));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await tx.CommitAsync(CancellationToken.None);

        if (bumped == 1)
        {
            signal.Signal();

            logger.LogInformation(
                "Backup batch {JobId} ran out of budget with {Count} action(s) left; continuing in a new batch",
                job.Id, remaining.Count);
        }
        else
        {
            logger.LogWarning(
                "Backup batch {JobId} ran out of budget with {Count} action(s) left, but its parent is no " +
                "longer running; the next run will re-plan them", job.Id, remaining.Count);
        }
    }

    private async Task EnqueueStandaloneContinuationAsync(
        ProcessingJob job, Guid backupSourceId, List<BackupAction> remaining)
    {
        db.ProcessingJobs.Add(BuildContinuation(job, backupSourceId, remaining, job.Priority));
        await db.SaveChangesAsync(CancellationToken.None);
        signal.Signal();
    }

    private static ProcessingJob BuildContinuation(
        ProcessingJob job, Guid backupSourceId, List<BackupAction> remaining, int priority)
    {
        var continuation = new ProcessingJob
        {
            Type = ProcessingJobType.BackupBatch,
            Status = ProcessingJobStatus.Pending,
            EntityType = job.EntityType,
            EntityId = job.EntityId,
            CreatedByUserId = job.CreatedByUserId,
            ParentJobId = job.ParentJobId,
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = priority,
        };

        continuation.SetPayload(new BackupBatchPayload(backupSourceId, remaining));
        return continuation;
    }

    private void Upsert(
        Guid backupSourceId,
        BackupAction action,
        string handle,
        Dictionary<Guid, DocumentBackupState> stateByVersion)
    {
        var now = timeProvider.GetUtcNow();

        if (stateByVersion.TryGetValue(action.VersionId, out var existing))
        {
            existing.RemoteRelativePath = action.RelativePath;
            existing.RemoteHandle = handle;
            existing.BackedUpHash = action.FileHash;
            existing.FileSize = action.FileSize;
            existing.BackedUpAt = now;
            return;
        }

        var state = new DocumentBackupState
        {
            BackupSourceId = backupSourceId,
            DocumentVersionId = action.VersionId,
            DocumentId = action.DocumentId,
            RemoteRelativePath = action.RelativePath,
            RemoteHandle = handle,
            BackedUpHash = action.FileHash,
            FileSize = action.FileSize,
            BackedUpAt = now,
        };

        db.DocumentBackupStates.Add(state);
        stateByVersion[action.VersionId] = state;
    }

    /// <summary>Streams the blob once, producing SHA-256 and (optionally) MD5 from the same read.</summary>
    private static async Task<(string Sha256, string? Md5)> ComputeHashesAsync(
        string path, bool alsoMd5, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);

        if (!alsoMd5)
            return (Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(), null);

        using var sha256 = SHA256.Create();
        using var md5 = MD5.Create();

        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            sha256.TransformBlock(buffer, 0, read, null, 0);
            md5.TransformBlock(buffer, 0, read, null, 0);
        }

        sha256.TransformFinalBlock([], 0, 0);
        md5.TransformFinalBlock([], 0, 0);

        return (
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant(),
            Convert.ToHexString(md5.Hash!).ToLowerInvariant());
    }

    // Best-effort content type from the destination extension. The local target ignores it; a cloud
    // target needs it to store the object with a usable MIME type.
    private static string ResolveContentType(string relativePath) =>
        ContentTypes.TryGetContentType(relativePath, out var contentType)
            ? contentType
            : "application/octet-stream";

    private async Task SaveQuietlyAsync()
    {
        try
        {
            // CancellationToken.None: we are already unwinding a cancellation, and the point of this save
            // is to keep the progress that did land.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist backup state after cancellation");
        }
    }
}
