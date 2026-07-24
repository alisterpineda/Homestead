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
public class BackupBatchJobHandler(
    AppDbContext db,
    IBackupTargetResolver targetResolver,
    IOptions<FileStorageOptions> storage,
    IProcessingNotifier notifier,
    ILogger<BackupBatchJobHandler> logger) : IJobHandler
{
    /// <summary>
    /// How many completed actions to accumulate before flushing state rows. A crash between a target
    /// write and its state row is harmless — the path is stable, so the next run simply re-copies — but
    /// losing a whole batch's worth of progress to one timeout would stall a mirror that can never quite
    /// finish. Small enough to bound that, large enough to avoid a round trip per file.
    /// </summary>
    private const int StateFlushInterval = 25;

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
        if (!target.IsAvailable)
            throw new InvalidOperationException(
                $"Backup target '{source.TargetKey}' is unavailable: {target.ConfigurationError}");

        var mediaPath = storage.Value.MediaPath;

        var versionIds = payload.Actions.Select(a => a.VersionId).ToList();
        var stateByVersion = await db.DocumentBackupStates
            .Where(s => s.BackupSourceId == payload.BackupSourceId
                        && versionIds.Contains(s.DocumentVersionId))
            .ToDictionaryAsync(s => s.DocumentVersionId, ct);

        var failures = new List<string>();
        var pendingSinceFlush = 0;

        foreach (var action in payload.Actions)
        {
            try
            {
                var didWork = action.Kind switch
                {
                    BackupActionKind.Put => await PutAsync(
                        target, payload.BackupSourceId, mediaPath, action, stateByVersion, ct),
                    BackupActionKind.Move => await MoveAsync(target, action, stateByVersion, ct),
                    BackupActionKind.Delete => await DeleteAsync(target, action, stateByVersion, ct),
                    _ => throw new InvalidOperationException($"Unknown backup action '{action.Kind}'.")
                };

                if (!didWork)
                    continue;

                if (++pendingSinceFlush >= StateFlushInterval)
                {
                    await db.SaveChangesAsync(ct);
                    pendingSinceFlush = 0;
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

                failures.Add($"{action.Kind} '{action.RelativePath}': {ex.Message}");
            }
        }

        // Persist everything that succeeded BEFORE reporting the failures. One bad file must not cost the
        // other ninety-nine their state rows — that is what turns a transient error into a permanent
        // re-copy loop.
        await db.SaveChangesAsync(ct);

        if (job.ParentJobId is null)
        {
            await notifier.NotifyJobStatusAsync(
                job.Id, ProcessingJobStatus.Completed,
                ProcessingEntityType.BackupSource, job.EntityId,
                documentName: $"Backup batch ({payload.Actions.Count} action(s))",
                error: null, job.CreatedByUserId, ct);
        }

        // Throwing here fails this batch, which surfaces at the parent as PartiallyCompleted rather than
        // as a silent success — the admin needs to see that some files did not make it.
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{failures.Count} of {payload.Actions.Count} backup action(s) failed: " +
                string.Join("; ", failures.Take(10)) +
                (failures.Count > 10 ? $" (+{failures.Count - 10} more)" : string.Empty));
        }
    }

    /// <summary>Copies a blob to the target and records it. Returns false when nothing was written.</summary>
    private async Task<bool> PutAsync(
        IBackupTarget target,
        Guid backupSourceId,
        string mediaPath,
        BackupAction action,
        Dictionary<Guid, DocumentBackupState> stateByVersion,
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
            return false;
        }

        // Verify BEFORE writing, which costs a second pass over the file but is the whole point: the
        // mirror is the copy of last resort, so propagating bytes that no longer match the recorded hash
        // would quietly replace a good backup with a corrupt one. The read is streamed, never buffered.
        var actualHash = await ComputeSha256Async(sourcePath, ct);

        if (!string.Equals(actualHash, action.FileHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Content hash mismatch for version {action.VersionId} at '{action.SourceFilePath}': " +
                $"expected {action.FileHash}, found {actualHash}. Refusing to copy corrupt content.");
        }

        string handle;
        await using (var content = File.OpenRead(sourcePath))
        {
            handle = await target.PutAsync(
                new BackupObjectRef(action.RelativePath, action.Handle),
                content,
                ResolveContentType(action.RelativePath),
                ct);
        }

        // Only now — after the write returned — does the state row claim the copy exists.
        Upsert(backupSourceId, action, handle, stateByVersion);
        return true;
    }

    private async Task<bool> MoveAsync(
        IBackupTarget target,
        BackupAction action,
        Dictionary<Guid, DocumentBackupState> stateByVersion,
        CancellationToken ct)
    {
        if (!stateByVersion.TryGetValue(action.VersionId, out var state))
        {
            logger.LogWarning(
                "Backup move for version {VersionId} has no state row, skipping", action.VersionId);
            return false;
        }

        try
        {
            var handle = await target.MoveAsync(
                new BackupObjectRef(action.FromRelativePath ?? state.RemoteRelativePath, state.RemoteHandle),
                action.RelativePath,
                ct);

            state.RemoteRelativePath = action.RelativePath;
            state.RemoteHandle = handle;
            state.BackedUpAt = DateTimeOffset.UtcNow;
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

        return true;
    }

    private async Task<bool> DeleteAsync(
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

        return true;
    }

    private void Upsert(
        Guid backupSourceId,
        BackupAction action,
        string handle,
        Dictionary<Guid, DocumentBackupState> stateByVersion)
    {
        var now = DateTimeOffset.UtcNow;

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
            Id = Guid.NewGuid(),
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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // Best-effort content type from the destination extension. The local target ignores it; a future
    // cloud target needs it to store the object with a usable MIME type.
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
