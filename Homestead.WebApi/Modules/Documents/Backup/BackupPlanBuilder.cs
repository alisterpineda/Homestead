using Homestead.WebApi.Infrastructure.Backup;

namespace Homestead.WebApi.Modules.Documents.Backup;

/// <summary>
/// Diffs the desired mirror layout against what the state table says is already there, applies the
/// bulk-delete circuit breaker, and packs the surviving actions into batches sized for the processor's
/// 5-minute per-job timeout. Pure — no I/O, no database.
/// </summary>
public static class BackupPlanBuilder
{
    public static BackupPlan Build(
        IReadOnlyList<BackupDesiredFile> desired,
        IReadOnlyList<BackupStateRow> existing,
        DocumentBackupEngineOptions options,
        BackupBatchCaps caps,
        bool allowLargePrune)
    {
        var existingByVersion = existing.ToDictionary(s => s.VersionId);
        var desiredVersionIds = desired.Select(d => d.VersionId).ToHashSet();

        var puts = new List<BackupAction>();
        var moves = new List<BackupAction>();
        var unchanged = 0;

        foreach (var file in desired)
        {
            if (!existingByVersion.TryGetValue(file.VersionId, out var state))
            {
                puts.Add(PutAction(file, handle: null));
                continue;
            }

            // Version blobs are immutable and content-addressed (document_versions.FileHash carries a
            // UNIQUE index), so a hash difference means the state row predates a change in what this
            // version id points at — replace rather than trust it.
            if (!string.Equals(state.BackedUpHash, file.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                puts.Add(PutAction(file, state.Handle));
                continue;
            }

            if (!string.Equals(state.RelativePath, file.RelativePath, StringComparison.Ordinal))
            {
                moves.Add(new BackupAction
                {
                    Kind = BackupActionKind.Move,
                    VersionId = file.VersionId,
                    DocumentId = file.DocumentId,
                    RelativePath = file.RelativePath,
                    FromRelativePath = state.RelativePath,
                    Handle = state.Handle,
                    FileHash = file.FileHash,
                    FileSize = file.FileSize,
                });
                continue;
            }

            // Right content, right place. This is the steady state after the first run: nothing is read,
            // nothing is copied, and the run costs one query.
            unchanged++;
        }

        // Deletions are found by anti-join against the state table, never by cascade — the state row is
        // the ONLY record of what a permanently-deleted version left behind in the mirror, which is
        // exactly why DocumentBackupState.DocumentVersionId carries no foreign key.
        var deletes = existing
            .Where(s => !desiredVersionIds.Contains(s.VersionId))
            .Select(s => new BackupAction
            {
                Kind = BackupActionKind.Delete,
                VersionId = s.VersionId,
                DocumentId = s.DocumentId,
                RelativePath = s.RelativePath,
                Handle = s.Handle,
                FileHash = s.BackedUpHash,
                FileSize = s.FileSize,
            })
            .OrderBy(a => a.RelativePath, StringComparer.Ordinal)
            .ToList();

        var pruneBlocked = !allowLargePrune && TripsBreaker(deletes.Count, existing.Count, options);
        var emittedDeletes = pruneBlocked ? [] : deletes;

        // Ordering is load-bearing and is Delete → Move → Put:
        //   • Deletes first free every path this run is about to reuse, so a later Move or Put can't land
        //     on an occupied path and then have the stale delete remove the file it just wrote.
        //   • Moves next, because a move's destination may be a path a delete just freed.
        //   • Puts last, when every destination is guaranteed vacant.
        // Batches inherit descending priority from their index (see BackupToTargetJobHandler), so the
        // worker CLAIMS them in this order. Claim order is only execution order at MaxConcurrency 1.
        //
        // KNOWN HAZARD, unfixed and NOT self-healing — the shipped MaxConcurrency is 4, so adjacent
        // batches of one run do overlap. When a Put's destination is also a Move's SOURCE (the ordinary
        // "new version demotes the old latest" shape: Move Doc.pdf -> _versions/Doc (v1).pdf, Put the new
        // Doc.pdf), a Put that lands first has its file relocated by the Move that follows. The result is
        // the new version's bytes sitting under the old version's name and nothing at all under the new
        // one — and because both state rows record the paths the planner intended, the next run diffs
        // them as unchanged and never repairs it. The hole is permanent and silent.
        //
        // Left alone here deliberately: the fix belongs to the run topology (serialize a run's batches,
        // or split the plan so a Move's source is never a Put's destination in the same run), not to the
        // packing order, and it predates the Drive target. Reproduces intermittently as
        // BackupMirrorTests.AddingAVersion_DemotesTheOldLatestIntoTheVersionsFolder.
        var batches = new List<BackupActionBatch>();
        batches.AddRange(PackMetadata(emittedDeletes, caps));
        batches.AddRange(PackMetadata(moves, caps));
        batches.AddRange(PackFiles(puts, caps));

        return new BackupPlan
        {
            Batches = batches,
            PutCount = puts.Count,
            MoveCount = moves.Count,
            DeleteCount = emittedDeletes.Count,
            BlockedDeleteCount = pruneBlocked ? deletes.Count : 0,
            PruneBlocked = pruneBlocked,
            TotalBytes = puts.Sum(p => p.FileSize),
            UnchangedCount = unchanged,
        };
    }

    private static BackupAction PutAction(BackupDesiredFile file, string? handle) => new()
    {
        Kind = BackupActionKind.Put,
        VersionId = file.VersionId,
        DocumentId = file.DocumentId,
        RelativePath = file.RelativePath,
        Handle = handle,
        SourceFilePath = file.SourceFilePath,
        FileHash = file.FileHash,
        FileSize = file.FileSize,
    };

    /// <summary>
    /// The bulk-delete circuit breaker: trips when the run wants to remove more than
    /// <c>max(MinAbsoluteDeletes, DeleteFraction × already-mirrored)</c> objects.
    /// </summary>
    /// <remarks>
    /// Two thresholds because either alone is wrong. A pure fraction trips constantly on a tiny library
    /// (3 of 20 files is 15%); a pure absolute never trips on a large one (500 of 50,000 is nothing, but
    /// 5,000 should stop the run). Strictly-greater, so a run sitting exactly on the threshold proceeds —
    /// the guard is against a runaway, not against ordinary housekeeping.
    /// </remarks>
    public static bool TripsBreaker(int deleteCount, int alreadyMirroredCount, DocumentBackupEngineOptions options)
    {
        var threshold = Math.Max(
            options.ResolveMinAbsoluteDeletes(),
            options.ResolveDeleteFraction() * alreadyMirroredCount);

        return deleteCount > threshold;
    }

    // Puts move bytes, so they are capped by BOTH file count and total size — one 4 GB video would
    // otherwise sit alone in a batch that still blows the 5-minute timeout, and 100 small scans would
    // finish in seconds. Whichever cap binds first wins. A single file larger than the byte cap still
    // gets its own batch rather than being dropped.
    private static List<BackupActionBatch> PackFiles(List<BackupAction> actions, BackupBatchCaps caps)
    {
        var maxFiles = caps.MaxFilesPerBatch;
        var maxBytes = caps.MaxBytesPerBatch;

        var batches = new List<BackupActionBatch>();
        var current = new List<BackupAction>();
        var currentBytes = 0L;

        foreach (var action in actions)
        {
            if (current.Count > 0 &&
                (current.Count >= maxFiles || currentBytes + action.FileSize > maxBytes))
            {
                batches.Add(new BackupActionBatch(current));
                current = [];
                currentBytes = 0;
            }

            current.Add(action);
            currentBytes += action.FileSize;
        }

        if (current.Count > 0)
            batches.Add(new BackupActionBatch(current));

        return batches;
    }

    // Moves and deletes are constant-time per operation — no bytes cross the wire — so they only need a
    // count cap, set an order of magnitude higher than the file cap.
    private static List<BackupActionBatch> PackMetadata(List<BackupAction> actions, BackupBatchCaps caps)
    {
        var maxOps = caps.MaxOpsPerMetadataBatch;

        return actions
            .Chunk(maxOps)
            .Select(chunk => new BackupActionBatch(chunk))
            .ToList();
    }
}
