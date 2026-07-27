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
        // Doubles as the desired-version SET the delete anti-join below tests against — one pass and one
        // hash structure over every version in the instance rather than two.
        var desiredByVersion = desired.ToDictionary(d => d.VersionId);

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
            .Where(s => !desiredByVersion.ContainsKey(s.VersionId))
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

        // A COPY, never an alias of `deletes`: ChainMoves appends relocation artifacts to this list, and
        // aliasing would make `deletes.Count` mean two different things either side of that call. The
        // counts below are what the summary and the operator-facing latch reason about, and neither knows
        // anything about relocation artifacts — so they are read off the pristine `deletes`.
        List<BackupAction> emittedDeletes = pruneBlocked ? [] : [.. deletes];

        var prunedCount = pruneBlocked ? 0 : deletes.Count;
        var blockedCount = pruneBlocked ? deletes.Count : 0;

        // Moves are the only actions that both FREE and OCCUPY a path, so they are the only ones that can
        // conflict with each other. Chaining resolves that; a rename cycle, which no ordering can resolve,
        // is broken here by downgrading one member into a Delete + a Put. The paired Delete is appended
        // AFTER the breaker check on purpose — it is a relocation artifact, not a prune, and counting it
        // would let a rename swap spuriously latch the operator-facing "N deletions were blocked" banner.
        var moveChains = ChainMoves(moves, desiredByVersion, emittedDeletes, puts);
        var orderedMoveCount = moveChains.Sum(c => c.Count);

        // The pipeline is Delete → Move → Put → Prune, and STAGE is what carries that — not list order,
        // and not a batch's index.
        //   • Deletes only ever FREE paths, and each path is deleted at most once, so no two deletes can
        //     conflict. Running them first frees every path this run is about to reuse, so no later Move
        //     or Put can land on an occupied path.
        //   • Moves free and occupy, so they CAN conflict; ChainMoves keeps every conflicting move inside
        //     one batch, in vacate-before-fill order, which makes distinct move batches independent too.
        //   • Puts only ever OCCUPY paths, and desired paths are unique per version, so no two puts can
        //     conflict. By the time they run every destination is guaranteed vacant.
        //   • The prune sweep runs dead last, after everything that could empty or refill a folder.
        // Hence: batches within a stage are mutually independent BY CONSTRUCTION and are free to run
        // concurrently; a lower stage runs strictly before a higher one.
        //
        // The queue is what enforces that. BackupToTargetJobHandler maps stage onto child job priority,
        // and among one parent's children the processor will not claim a job while a strictly
        // higher-priority sibling is still Pending or InProgress — so the edge is a real happens-before,
        // not a scheduling hint, and it holds at any MaxConcurrency. A failed batch additionally cancels
        // its lower-priority siblings, so a failed Move can no longer be followed by the Put that assumed
        // it had happened.
        var batches = new List<BackupActionBatch>();
        batches.AddRange(PackDeletes(emittedDeletes, caps));
        batches.AddRange(PackChains(moveChains, caps));
        batches.AddRange(PackFiles(puts, caps));

        return new BackupPlan
        {
            Batches = batches,
            // The summary reports INTENT, the batches report MECHANISM. A move downgraded to break a
            // cycle is reported as a Put — the mirror really does re-copy those bytes and the admin should
            // see them — while its paired Delete is not reported at all, because nothing is being pruned.
            PutCount = puts.Count,
            MoveCount = orderedMoveCount,
            DeleteCount = prunedCount,
            BlockedDeleteCount = blockedCount,
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

    /// <summary>
    /// Orders the run's moves into dependency chains, breaking any rename cycle by downgrading one of its
    /// members into a <c>Delete</c> + a <c>Put</c>. Returns the chains, each already in execution order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constraint is per-path: whatever VACATES path P runs before whatever FILLS P. Between two moves
    /// that means N runs before M when <c>N.FromRelativePath == M.RelativePath</c>.
    /// </para>
    /// <para>
    /// Move sources are state paths and move destinations are desired paths, and both sets are unique — so
    /// every node has at most one predecessor and at most one successor. The graph is therefore a disjoint
    /// union of simple chains and simple cycles, and no general topological sort is needed: walk each
    /// chain from its head, and whatever is left over is, by construction, a cycle.
    /// </para>
    /// </remarks>
    private static List<List<BackupAction>> ChainMoves(
        List<BackupAction> moves,
        Dictionary<Guid, BackupDesiredFile> desiredByVersion,
        List<BackupAction> deletes,
        List<BackupAction> puts)
    {
        if (moves.Count == 0)
            return [];

        // Built by indexer rather than ToDictionary: a duplicate would mean a corrupt state table, and
        // degrading the ordering beats throwing out of a pure planner. The traversals below are guarded
        // by `visited` regardless, so a malformed graph cannot spin.
        var bySource = new Dictionary<string, BackupAction>(StringComparer.Ordinal);
        var byDest = new Dictionary<string, BackupAction>(StringComparer.Ordinal);
        foreach (var move in moves)
        {
            bySource[move.FromRelativePath ?? string.Empty] = move;
            byDest[move.RelativePath] = move;
        }

        // predecessor(M) = the move that vacates M's destination; successor(M) = the move that fills M's
        // source. A head is a move nothing has to run before.
        BackupAction? Successor(BackupAction move) =>
            byDest.GetValueOrDefault(move.FromRelativePath ?? string.Empty);

        var visited = new HashSet<Guid>();
        var chains = new List<List<BackupAction>>();

        var heads = moves
            .Where(m => !bySource.ContainsKey(m.RelativePath))
            .OrderBy(m => m.FromRelativePath, StringComparer.Ordinal);

        foreach (var head in heads)
        {
            var chain = new List<BackupAction>();
            for (var cursor = head; cursor is not null && visited.Add(cursor.VersionId); cursor = Successor(cursor))
                chain.Add(cursor);

            if (chain.Count > 0)
                chains.Add(chain);
        }

        // Everything still unvisited has a predecessor, which — with in-degree at most one — means it sits
        // on a cycle. Iterating the leftovers in ascending source order makes the first node reached for a
        // given cycle the one with the smallest FromRelativePath in it, which is the member we break on.
        var leftovers = moves
            .Where(m => !visited.Contains(m.VersionId))
            .OrderBy(m => m.FromRelativePath, StringComparer.Ordinal)
            .ToList();

        foreach (var victim in leftovers)
        {
            if (!visited.Add(victim.VersionId))
                continue;

            var rest = new List<BackupAction>();
            for (var cursor = Successor(victim);
                 cursor is not null && cursor.VersionId != victim.VersionId && visited.Add(cursor.VersionId);
                 cursor = Successor(cursor))
                rest.Add(cursor);

            // The Delete is mandatory, not cosmetic. Locally File.Move(overwrite: true) would paper over
            // its absence, but on an id-addressed destination the object's identity is separate from its
            // path: without it the old file id keeps its name and the folder ends up holding two files
            // called the same thing. Same reason the Put below must CLEAR the handle — reusing it would
            // upload onto the very object the surviving move is relocating.
            deletes.Add(new BackupAction
            {
                Kind = BackupActionKind.Delete,
                VersionId = victim.VersionId,
                DocumentId = victim.DocumentId,
                RelativePath = victim.FromRelativePath ?? string.Empty,
                Handle = victim.Handle,
                FileHash = victim.FileHash,
                FileSize = victim.FileSize,
            });

            if (desiredByVersion.TryGetValue(victim.VersionId, out var file))
                puts.Add(PutAction(file, handle: null));

            if (rest.Count > 0)
                chains.Add(rest);
        }

        return chains;
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
                batches.Add(new BackupActionBatch(BackupStage.Put, current));
                current = [];
                currentBytes = 0;
            }

            current.Add(action);
            currentBytes += action.FileSize;
        }

        if (current.Count > 0)
            batches.Add(new BackupActionBatch(BackupStage.Put, current));

        return batches;
    }

    // Deletes are constant-time per operation — no bytes cross the wire — so they only need a count cap,
    // set an order of magnitude higher than the file cap. No chaining either: a delete only ever FREES a
    // path and each path is deleted at most once, so any split across batches is safe.
    private static List<BackupActionBatch> PackDeletes(List<BackupAction> actions, BackupBatchCaps caps) =>
        actions
            .Chunk(caps.MaxOpsPerMetadataBatch)
            .Select(chunk => new BackupActionBatch(BackupStage.Delete, chunk))
            .ToList();

    // Moves carry the same count cap, but a chain is atomic: its members must run in order, and only a
    // shared batch guarantees that, since same-stage batches are explicitly free to run concurrently. So
    // batches fill up to the cap on chain boundaries only. A chain longer than the cap gets its own
    // oversized batch — the same concession PackFiles already makes for a file larger than the byte cap.
    private static List<BackupActionBatch> PackChains(
        List<List<BackupAction>> chains, BackupBatchCaps caps)
    {
        var maxOps = caps.MaxOpsPerMetadataBatch;

        var batches = new List<BackupActionBatch>();
        var current = new List<BackupAction>();

        foreach (var chain in chains)
        {
            if (current.Count > 0 && current.Count + chain.Count > maxOps)
            {
                batches.Add(new BackupActionBatch(BackupStage.Move, current));
                current = [];
            }

            current.AddRange(chain);
        }

        if (current.Count > 0)
            batches.Add(new BackupActionBatch(BackupStage.Move, current));

        return batches;
    }
}
