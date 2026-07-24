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
            [Desired(versionId, "2025/Doc.pdf")], [], Options(), allowLargePrune: false);

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
            Options(), allowLargePrune: false);

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
            Options(), allowLargePrune: false);

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
            Options(), allowLargePrune: false);

        Assert.Empty(Actions(plan));
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(0, plan.ActionCount);
    }

    [Fact]
    public void StateRowWithNoSurvivingVersion_EmitsADelete()
    {
        var gone = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [], [State(gone, "2025/Gone.pdf")], Options(), allowLargePrune: false);

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
            Options(), allowLargePrune: false);

        Assert.Empty(Actions(plan));
    }

    [Fact]
    public void HashComparisonIsCaseInsensitive()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf", hash: "ABCDEF")],
            [State(versionId, "2025/Doc.pdf", hash: "abcdef")],
            Options(), allowLargePrune: false);

        Assert.Empty(Actions(plan));
    }

    // --- Circuit breaker ---

    [Fact]
    public void DeletesExactlyAtTheAbsoluteThreshold_DoNotTrip()
    {
        var existing = Enumerable.Range(0, 25)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build([], existing, Options(minAbsoluteDeletes: 25), allowLargePrune: false);

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

        var plan = BackupPlanBuilder.Build([], existing, Options(minAbsoluteDeletes: 25), allowLargePrune: false);

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

        var atThreshold = BackupPlanBuilder.Build(keep, existing, Options(), allowLargePrune: false);
        Assert.False(atThreshold.PruneBlocked);
        Assert.Equal(100, atThreshold.DeleteCount);

        var overThreshold = BackupPlanBuilder.Build(
            keep.Skip(1).ToList(), existing, Options(), allowLargePrune: false);
        Assert.True(overThreshold.PruneBlocked);
        Assert.Equal(101, overThreshold.BlockedDeleteCount);
    }

    [Fact]
    public void AllowLargePrune_BypassesTheBreaker()
    {
        var existing = Enumerable.Range(0, 500)
            .Select(i => State(Guid.NewGuid(), $"2025/Doc{i}.pdf"))
            .ToList();

        var plan = BackupPlanBuilder.Build([], existing, Options(), allowLargePrune: true);

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

        var plan = BackupPlanBuilder.Build(desired, existing, Options(), allowLargePrune: false);

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

        var plan = BackupPlanBuilder.Build(desired, [], Options(), allowLargePrune: false);

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

        var plan = BackupPlanBuilder.Build(desired, [], Options(maxFilesPerBatch: 100), allowLargePrune: false);

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
            desired, [], Options(maxFilesPerBatch: 100, maxBytesPerBatch: 1000), allowLargePrune: false);

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
            [], Options(maxBytesPerBatch: 1000), allowLargePrune: false);

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
            [], existing, Options(maxOpsPerMetadataBatch: 5), allowLargePrune: true);

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
            Options(), allowLargePrune: false);

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
            Options(), allowLargePrune: false);

        Assert.Equal(100, plan.TotalBytes);
    }

    [Fact]
    public void NothingToDo_ProducesNoBatches()
    {
        var versionId = Guid.NewGuid();

        var plan = BackupPlanBuilder.Build(
            [Desired(versionId, "2025/Doc.pdf")],
            [State(versionId, "2025/Doc.pdf")],
            Options(), allowLargePrune: false);

        Assert.Empty(plan.Batches);
    }
}
