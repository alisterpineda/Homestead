using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Documents.Backup;

namespace Homestead.WebApi.Tests.Unit.Documents.Backup;

public class BackupPlanBuilderTests
{
    private static DocumentBackupEngineOptions Options(
        int minAbsoluteDeletes = 25,
        double deleteFraction = 0.10,
        int maxFilesPerBatch = 100,
        long maxBytesPerBatch = 512L * 1024 * 1024,
        int maxOpsPerMetadataBatch = 500) => new()
    {
        MinAbsoluteDeletes = minAbsoluteDeletes,
        DeleteFraction = deleteFraction,
        MaxFilesPerBatch = maxFilesPerBatch,
        MaxBytesPerBatch = maxBytesPerBatch,
        MaxOpsPerMetadataBatch = maxOpsPerMetadataBatch,
    };

    // The caps a target with no opinion produces: the engine options straight through. Kept as a helper
    // so every call site reads the same way as before the per-target override seam was added.
    private static BackupBatchCaps Caps(DocumentBackupEngineOptions options) => BackupBatchCaps.Resolve(
        options.MaxFilesPerBatch, options.MaxBytesPerBatch, options.MaxOpsPerMetadataBatch, default);

    // The caps seam: two independent things set these numbers, and the destination wins where it states
    // one — a residential uplink makes the source kind's disk-sized caps wildly wrong.
    [Fact]
    public void Caps_ATargetOverrideBeatsTheSourceKindsOwnCap()
    {
        var caps = BackupBatchCaps.Resolve(
            kindMaxFilesPerBatch: 100,
            kindMaxBytesPerBatch: 512L * 1024 * 1024,
            kindMaxOpsPerMetadataBatch: 500,
            new BackupBatchCapOverrides(25, 64L * 1024 * 1024, null));

        Assert.Equal(25, caps.MaxFilesPerBatch);
        Assert.Equal(64L * 1024 * 1024, caps.MaxBytesPerBatch);
        // Null defers to the kind rather than to zero.
        Assert.Equal(500, caps.MaxOpsPerMetadataBatch);
    }

    // A degenerate value would produce zero-capacity batches and an infinite packing loop, so every cap
    // is clamped on the way through rather than trusted from config.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Caps_ADegenerateValueIsFloored(int degenerate)
    {
        var fromKind = BackupBatchCaps.Resolve(degenerate, degenerate, degenerate, default);
        Assert.Equal(1, fromKind.MaxFilesPerBatch);
        Assert.Equal(1, fromKind.MaxBytesPerBatch);
        Assert.Equal(1, fromKind.MaxOpsPerMetadataBatch);

        var fromOverride = BackupBatchCaps.Resolve(
            100, 100, 100, new BackupBatchCapOverrides(degenerate, degenerate, degenerate));
        Assert.Equal(1, fromOverride.MaxFilesPerBatch);
        Assert.Equal(1, fromOverride.MaxBytesPerBatch);
        Assert.Equal(1, fromOverride.MaxOpsPerMetadataBatch);
    }

    private static BackupDesiredFile Desired(
        Guid versionId, string path, string hash = "hash", long size = 1024, Guid? documentId = null) =>
        new(versionId, documentId ?? Guid.NewGuid(), path, hash, $"documents/files/aaaa/{hash}.pdf", size);

    private static BackupStateRow State(
        Guid versionId, string path, string hash = "hash", long size = 1024, Guid? documentId = null) =>
        new(versionId, documentId ?? Guid.NewGuid(), path, path, hash, size);

    private static List<BackupAction> Actions(BackupPlan plan) =>
        plan.Batches.SelectMany(b => b.Actions).ToList();

    [Fact]
    public void NoStateRow_EmitsAPut()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf")], [], Options(), Caps(Options()), allowLargePrune: false);

        var action = Assert.Single(Actions(plan));
        Assert.Equal(BackupActionKind.Put, action.Kind);
        Assert.Equal("2025/Doc.pdf", action.RelativePath);
        Assert.Equal(1, plan.PutCount);
    }

    [Fact]
    public void ChangedHash_EmitsAReplacementPutCarryingTheExistingHandle()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf", hash: "new")],
            [State(versionId, "2025/Doc.pdf", hash: "old")],
            Options(), Caps(Options()), allowLargePrune: false);

        var action = Assert.Single(Actions(plan));
        Assert.Equal(BackupActionKind.Put, action.Kind);
        Assert.Equal("new", action.FileHash);
        Assert.Equal("2025/Doc.pdf", action.Handle);
    }

    [Fact]
    public void SameHashDifferentPath_EmitsAMoveNotAReCopy()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2026/Doc.pdf")],
            [State(versionId, "2025/Doc.pdf")],
            Options(), Caps(Options()), allowLargePrune: false);

        var action = Assert.Single(Actions(plan));
        Assert.Equal(BackupActionKind.Move, action.Kind);
        Assert.Equal("2025/Doc.pdf", action.FromRelativePath);
        Assert.Equal("2026/Doc.pdf", action.RelativePath);
        Assert.Equal(1, plan.MoveCount);
        Assert.Equal(0, plan.PutCount);
    }

    [Fact]
    public void SameHashSamePath_EmitsNothing()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf")],
            [State(versionId, "2025/Doc.pdf")],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Empty(Actions(plan));
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(0, plan.ActionCount);
    }

    [Fact]
    public void StateRowWithNoSurvivingVersion_EmitsADelete()
    {
        var gone = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [], [State(gone, "2025/Gone.pdf")], Options(), Caps(Options()), allowLargePrune: false);

        var action = Assert.Single(Actions(plan));
        Assert.Equal(BackupActionKind.Delete, action.Kind);
        Assert.Equal("2025/Gone.pdf", action.RelativePath);
        Assert.Equal(1, plan.DeleteCount);
    }

    // Trash is a restorable state, so a trashed document's versions stay in the desired set and the diff
    // sees nothing to do. Only a PERMANENT delete removes the version row and produces a mirror delete.
    [Fact]
    public void TrashedDocumentStillInTheDesiredSet_EmitsNothing()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Trashed.pdf")],
            [State(versionId, "2025/Trashed.pdf")],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Empty(Actions(plan));
    }

    [Fact]
    public void HashComparisonIsCaseInsensitive()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf", hash: "ABCDEF")],
            [State(versionId, "2025/Doc.pdf", hash: "abcdef")],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Empty(Actions(plan));
    }

    // --- Circuit breaker ---

    [Fact]
    public void DeletesExactlyAtTheAbsoluteThreshold_DoNotTrip()
    {
        var existing = Enumerable.Range(0, 25)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build([], existing, Options(minAbsoluteDeletes: 25), Caps(Options(minAbsoluteDeletes: 25)), allowLargePrune: false);

        Assert.False(plan.PruneBlocked);
        Assert.Equal(25, plan.DeleteCount);
        Assert.Equal(0, plan.BlockedDeleteCount);
    }

    [Fact]
    public void OneDeleteOverTheAbsoluteThreshold_Trips()
    {
        var existing = Enumerable.Range(0, 26)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build([], existing, Options(minAbsoluteDeletes: 25), Caps(Options(minAbsoluteDeletes: 25)), allowLargePrune: false);

        Assert.True(plan.PruneBlocked);
        Assert.Equal(0, plan.DeleteCount);
        Assert.Equal(26, plan.BlockedDeleteCount);
    }

    // With a large mirror the fraction binds before the absolute floor: 10% of 1000 is 100, so 100 is
    // fine and 101 is not.
    [Fact]
    public void FractionThresholdBindsOnALargeMirror()
    {
        var existing = Enumerable.Range(0, 1000)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var keep = existing.Skip(100)
            .Select(s => Desired(s.VersionId, s.RelativePath))
            .ToList();

        var atThreshold = BackupPlanBuilder.Build(keep, existing, Options(), Caps(Options()), allowLargePrune: false);
        Assert.False(atThreshold.PruneBlocked);
        Assert.Equal(100, atThreshold.DeleteCount);

        var overThreshold = BackupPlanBuilder.Build(
            keep.Skip(1).ToList(), existing, Options(), Caps(Options()), allowLargePrune: false);
        Assert.True(overThreshold.PruneBlocked);
        Assert.Equal(101, overThreshold.BlockedDeleteCount);
    }

    [Fact]
    public void AllowLargePrune_BypassesTheBreaker()
    {
        var existing = Enumerable.Range(0, 500)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build([], existing, Options(), Caps(Options()), allowLargePrune: true);

        Assert.False(plan.PruneBlocked);
        Assert.Equal(500, plan.DeleteCount);
    }

    // The whole point of the breaker: a suspicious delete wave must not also stop the mirror from picking
    // up new and renamed files. Blocking prunes protects data; blocking puts would lose it.
    [Fact]
    public void WhenTheBreakerTrips_PutsAndMovesAreStillEmitted()
    {
        var existing = Enumerable.Range(0, 100)
            .Select(i => State(Guid.NewGuid(), $"2025/Old{i}.pdf"))
            .ToList();

        var moved = existing[0];
        var desired = new List<BackupDesiredFile>
        {
            Desired(moved.VersionId, "2026/Old0.pdf"),
            Desired(Guid.NewGuid(), "2026/BrandNew.pdf"),
        };

        var plan = BackupPlanBuilder.Build(desired, existing, Options(), Caps(Options()), allowLargePrune: false);

        Assert.True(plan.PruneBlocked);
        Assert.Equal(0, plan.DeleteCount);
        Assert.Equal(99, plan.BlockedDeleteCount);
        Assert.Equal(1, plan.PutCount);
        Assert.Equal(1, plan.MoveCount);

        var kinds = Actions(plan).Select(a => a.Kind).ToList();
        Assert.Contains(BackupActionKind.Put, kinds);
        Assert.Contains(BackupActionKind.Move, kinds);
        Assert.DoesNotContain(BackupActionKind.Delete, kinds);
    }

    [Fact]
    public void EmptyMirror_NeverTripsOnItsFirstRun()
    {
        var desired = Enumerable.Range(0, 5_000)
            .Select(i => Desired(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build(desired, [], Options(), Caps(Options()), allowLargePrune: false);

        Assert.False(plan.PruneBlocked);
        Assert.Equal(5_000, plan.PutCount);
    }

    // --- Batch packing ---

    [Fact]
    public void PutsArePackedByFileCount()
    {
        var desired = Enumerable.Range(0, 250)
            .Select(i => Desired(Guid.NewGuid(), $"2025/Doc{i:D4}.pdf", size: 1))
            .ToList();

        var plan = BackupPlanBuilder.Build(desired, [], Options(maxFilesPerBatch: 100), Caps(Options(maxFilesPerBatch: 100)), allowLargePrune: false);

        Assert.Equal(3, plan.Batches.Count);
        Assert.Equal([100, 100, 50], plan.Batches.Select(b => b.Actions.Count));
    }

    [Fact]
    public void PutsArePackedByByteBudgetWhenItBindsFirst()
    {
        var desired = Enumerable.Range(0, 10)
            .Select(i => Desired(Guid.NewGuid(), $"2025/Doc{i}.pdf", size: 400))
            .ToList();

        var plan = BackupPlanBuilder.Build(
            desired, [], Options(maxFilesPerBatch: 100, maxBytesPerBatch: 1000), Caps(Options(maxFilesPerBatch: 100, maxBytesPerBatch: 1000)), allowLargePrune: false);

        Assert.All(plan.Batches, b => Assert.True(b.Actions.Sum(a => a.FileSize) <= 1000));
        Assert.Equal(10, plan.Batches.Sum(b => b.Actions.Count));
    }

    // A single file larger than the whole byte budget must still be attempted, not silently dropped.
    [Fact]
    public void AFileLargerThanTheByteBudget_GetsItsOwnBatch()
    {
        var plan = BackupPlanBuilder.Build(
            [
                Desired(Guid.NewGuid(), "2025/Small.pdf", size: 10),
                Desired(Guid.NewGuid(), "2025/Whale.mov", size: 5_000),
            ],
            [], Options(maxBytesPerBatch: 1000), Caps(Options(maxBytesPerBatch: 1000)), allowLargePrune: false);

        Assert.Equal(2, plan.Batches.Count);
        Assert.Equal(2, plan.Batches.Sum(b => b.Actions.Count));
    }

    [Fact]
    public void MetadataOpsArePackedByTheirOwnCountCap()
    {
        var existing = Enumerable.Range(0, 12)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i:D2}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build(
            [], existing, Options(maxOpsPerMetadataBatch: 5), Caps(Options(maxOpsPerMetadataBatch: 5)), allowLargePrune: true);

        Assert.Equal(3, plan.Batches.Count);
        Assert.Equal([5, 5, 2], plan.Batches.Select(b => b.Actions.Count));
    }

    // Ordering is load-bearing: deletes free paths that moves and puts are about to reuse, so a stale
    // delete can never remove a file this same run just wrote.
    [Fact]
    public void BatchesAreOrderedDeletesThenMovesThenPuts()
    {
        var movedVersion = Guid.NewGuid();
        var goneVersion = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [
                Desired(movedVersion, "2026/Moved.pdf"),
                Desired(Guid.NewGuid(), "2026/New.pdf"),
            ],
            [
                State(movedVersion, "2025/Moved.pdf"),
                State(goneVersion, "2025/Gone.pdf"),
            ],
            Options(), Caps(Options()), allowLargePrune: false);

        var kinds = Actions(plan).Select(a => a.Kind).ToList();
        Assert.Equal(
            [BackupActionKind.Delete, BackupActionKind.Move, BackupActionKind.Put],
            kinds);
    }

    [Fact]
    public void TotalBytesCountsOnlyPuts()
    {
        var movedVersion = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [
                Desired(movedVersion, "2026/Moved.pdf", size: 900),
                Desired(Guid.NewGuid(), "2026/New.pdf", size: 100),
            ],
            [State(movedVersion, "2025/Moved.pdf", size: 900)],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Equal(100, plan.TotalBytes);
    }

    [Fact]
    public void NothingToDo_ProducesNoBatches()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf")],
            [State(versionId, "2025/Doc.pdf")],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Empty(plan.Batches);
    }

    // --- Stages and move ordering ---

    // Stage is the ordering contract at the type level. BackupToTargetJobHandler maps it onto child job
    // priority, and the processor turns a strictly higher sibling priority into a happens-before edge —
    // so a batch's stage, not its position in the list, is what actually orders the run.
    [Fact]
    public void EveryBatchCarriesTheStageMatchingItsActions()
    {
        var movedVersion = Guid.NewGuid();
        var goneVersion = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [
                Desired(movedVersion, "2026/Moved.pdf"),
                Desired(Guid.NewGuid(), "2026/New.pdf"),
            ],
            [
                State(movedVersion, "2025/Moved.pdf"),
                State(goneVersion, "2025/Gone.pdf"),
            ],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Equal(
            [BackupStage.Delete, BackupStage.Move, BackupStage.Put],
            plan.Batches.Select(b => b.Stage));

        Assert.All(plan.Batches, b => Assert.All(b.Actions, a => Assert.Equal(
            b.Stage switch
            {
                BackupStage.Delete => BackupActionKind.Delete,
                BackupStage.Move => BackupActionKind.Move,
                _ => BackupActionKind.Put,
            },
            a.Kind)));
    }

    // Within the move stage the constraint is per-path: whatever VACATES a path runs before whatever FILLS
    // it. With A→B and B→C that means B→C first, or the A→B lands on a path B still occupies.
    [Fact]
    public void AMoveChain_IsEmittedVacateBeforeFill()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [
                Desired(first, "2025/B.pdf"),
                Desired(second, "2025/C.pdf"),
            ],
            [
                State(first, "2025/A.pdf"),
                State(second, "2025/B.pdf"),
            ],
            Options(), Caps(Options()), allowLargePrune: false);

        var moves = Actions(plan);
        Assert.Equal(2, moves.Count);
        Assert.All(moves, m => Assert.Equal(BackupActionKind.Move, m.Kind));
        Assert.Equal(["2025/B.pdf", "2025/A.pdf"], moves.Select(m => m.FromRelativePath));
    }

    // Same-stage batches are explicitly free to run concurrently, so a chain split across two of them
    // would lose exactly the ordering the chaining just established. The cap therefore only ever falls on
    // a chain boundary — a chain longer than the cap gets its own oversized batch instead.
    [Fact]
    public void AChain_IsNeverSplitAcrossBatchesEvenWhenTheCapFallsInsideIt()
    {
        var loner = Guid.NewGuid();
        var chainTail = Guid.NewGuid();
        var chainHead = Guid.NewGuid();

        var options = Options(maxOpsPerMetadataBatch: 2);

        var plan = BackupPlanBuilder.Build(
            [
                Desired(loner, "2025/A1.pdf"),
                Desired(chainTail, "2025/N.pdf"),
                Desired(chainHead, "2025/O.pdf"),
            ],
            [
                State(loner, "2025/A0.pdf"),
                State(chainTail, "2025/M.pdf"),
                State(chainHead, "2025/N.pdf"),
            ],
            options, Caps(options), allowLargePrune: false);

        // The standalone move is flushed on its own rather than taking one slot and pushing the chain's
        // second member into the next batch.
        Assert.Equal([1, 2], plan.Batches.Select(b => b.Actions.Count));
        Assert.All(plan.Batches, b => Assert.Equal(BackupStage.Move, b.Stage));

        Assert.Equal(["2025/A0.pdf"], plan.Batches[0].Actions.Select(a => a.FromRelativePath));
        Assert.Equal(["2025/N.pdf", "2025/M.pdf"], plan.Batches[1].Actions.Select(a => a.FromRelativePath));
    }

    // A rename swap is a cycle, and no ordering fixes a cycle — only the plan can. One member is
    // downgraded to a Delete + a Put, which the stage order then sequences: free the path, move the
    // survivor into it, re-copy the downgraded one fresh.
    [Fact]
    public void ARenameSwap_BreaksIntoADeleteAndAHandlelessPut()
    {
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [
                Desired(alpha, "2025/Beta.pdf"),
                Desired(beta, "2025/Alpha.pdf"),
            ],
            [
                State(alpha, "2025/Alpha.pdf"),
                State(beta, "2025/Beta.pdf"),
            ],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Equal(
            [BackupStage.Delete, BackupStage.Move, BackupStage.Put],
            plan.Batches.Select(b => b.Stage));

        // Broken on the smallest source path, ordinal — deterministic, like everything else the planner
        // emits.
        var delete = Assert.Single(plan.Batches[0].Actions);
        Assert.Equal(BackupActionKind.Delete, delete.Kind);
        Assert.Equal("2025/Alpha.pdf", delete.RelativePath);
        Assert.Equal(alpha, delete.VersionId);
        // Carries the handle, so an id-addressed destination removes the right object rather than leaving
        // a second file with the same name behind.
        Assert.Equal("2025/Alpha.pdf", delete.Handle);

        var move = Assert.Single(plan.Batches[1].Actions);
        Assert.Equal(BackupActionKind.Move, move.Kind);
        Assert.Equal(beta, move.VersionId);
        Assert.Equal("2025/Beta.pdf", move.FromRelativePath);
        Assert.Equal("2025/Alpha.pdf", move.RelativePath);

        var put = Assert.Single(plan.Batches[2].Actions);
        Assert.Equal(BackupActionKind.Put, put.Kind);
        Assert.Equal(alpha, put.VersionId);
        Assert.Equal("2025/Beta.pdf", put.RelativePath);
        // Handle cleared: reusing it would upload onto the very object the surviving move is relocating.
        Assert.Null(put.Handle);
        Assert.NotNull(put.SourceFilePath);
    }

    // A cycle-break delete is a relocation artifact, not a prune. Counting it would let an ordinary rename
    // swap latch the operator-facing "N deletions were blocked" banner on a run sitting exactly on the
    // threshold — a warning about data loss raised by two documents trading names.
    [Fact]
    public void ACycleBreakDelete_DoesNotCountTowardTheBreaker()
    {
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();

        var existing = Enumerable.Range(0, 25)
            .Select(i => State(Guid.NewGuid(), $"2025/Gone{i:D2}.pdf"))
            .Append(State(alpha, "2025/Alpha.pdf"))
            .Append(State(beta, "2025/Beta.pdf"))
            .ToList();

        var options = Options(minAbsoluteDeletes: 25);

        var plan = BackupPlanBuilder.Build(
            [
                Desired(alpha, "2025/Beta.pdf"),
                Desired(beta, "2025/Alpha.pdf"),
            ],
            existing, options, Caps(options), allowLargePrune: false);

        // 25 real prunes sits exactly on the threshold and proceeds; the swap's 26th delete is emitted but
        // never counted, so the breaker stays untripped.
        Assert.False(plan.PruneBlocked);
        Assert.Equal(25, plan.DeleteCount);
        Assert.Equal(0, plan.BlockedDeleteCount);
        Assert.Equal(26, Actions(plan).Count(a => a.Kind == BackupActionKind.Delete));
    }

    // The summary reports INTENT, the batches report MECHANISM. The mirror really does re-copy the
    // downgraded version's bytes, so the admin sees a put and its cost; nothing is being pruned, so the
    // paired delete is invisible.
    [Fact]
    public void ADowngradedMove_IsSummarizedAsAPutAndNotAsADelete()
    {
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [
                Desired(alpha, "2025/Beta.pdf", size: 900),
                Desired(beta, "2025/Alpha.pdf", size: 100),
            ],
            [
                State(alpha, "2025/Alpha.pdf", size: 900),
                State(beta, "2025/Beta.pdf", size: 100),
            ],
            Options(), Caps(Options()), allowLargePrune: false);

        Assert.Equal(1, plan.PutCount);
        Assert.Equal(1, plan.MoveCount);
        Assert.Equal(0, plan.DeleteCount);
        Assert.Equal(900, plan.TotalBytes);
        Assert.Equal(2, plan.ActionCount);
    }
}
