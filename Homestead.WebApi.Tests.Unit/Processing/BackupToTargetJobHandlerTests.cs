using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class BackupToTargetJobHandlerTests : IDisposable
{
    private const string TargetKey = "local";

    private readonly TestAppDbContext _db;
    private readonly IJobSignal _signal;
    private readonly IBackupTargetResolver _resolver;
    private readonly DocumentBackupEngineOptions _options = new();
    private readonly BackupToTargetJobHandler _sut;

    /// <summary>The pairing every test runs against, seeded by the constructor.</summary>
    private readonly Guid _sourceId = Guid.NewGuid();

    public BackupToTargetJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _signal = Substitute.For<IJobSignal>();
        _resolver = Substitute.For<IBackupTargetResolver>();
        _resolver.Resolve(Arg.Any<string>()).Returns(new StubTarget(TargetKey, available: true));

        _db.BackupSources.Add(new BackupSource
        {
            Id = _sourceId,
            SourceKind = DocumentsBackupSourceDescriptor.SourceKey,
            TargetKey = TargetKey,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _sut = new BackupToTargetJobHandler(
            _db, _resolver, Options.Create(_options), _signal,
            NullLogger<BackupToTargetJobHandler>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private ProcessingJob CreateJob(bool dryRun = false, bool allowLargePrune = false, Guid? sourceId = null)
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        job.SetPayload(new BackupRunPayload(sourceId ?? _sourceId, dryRun, allowLargePrune));
        return job;
    }

    private Guid SeedDocument(string name = "Doc", DateOnly? documentDate = null, int versionCount = 1)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            Name = name,
            DocumentDate = documentDate,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        for (var i = 1; i <= versionCount; i++)
        {
            var hash = Guid.NewGuid().ToString("N");
            _db.DocumentVersions.Add(new DocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                VersionNumber = i,
                ContentType = "application/pdf",
                FileSize = 1024,
                FilePath = DocumentVersion.GetFilePath(hash, ".pdf"),
                FileHash = hash,
                FileExtension = ".pdf",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _db.SaveChanges();
        return docId;
    }

    private void SeedMirroredState(int count, string pathPrefix = "Documents/2025")
    {
        for (var i = 0; i < count; i++)
        {
            _db.DocumentBackupStates.Add(new DocumentBackupState
            {
                Id = Guid.NewGuid(),
                BackupSourceId = _sourceId,
                DocumentVersionId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                RemoteRelativePath = $"{pathPrefix}/Gone{i}.pdf",
                BackedUpHash = Guid.NewGuid().ToString("N"),
                FileSize = 10,
                BackedUpAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private async Task<List<ProcessingJob>> ChildrenOf(ProcessingJob job, CancellationToken ct) =>
        await _db.ProcessingJobs.Where(j => j.ParentJobId == job.Id).ToListAsync(ct);

    private async Task<BackupSource> ReloadSourceAsync(CancellationToken ct) =>
        await _db.BackupSources.AsNoTracking().SingleAsync(s => s.Id == _sourceId, ct);

    [Fact]
    public async Task HandleAsync_FansOutOneChildPerBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        _options.MaxFilesPerBatch = 2;
        SeedDocument("A", new DateOnly(2025, 1, 1), versionCount: 3);
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        var children = await ChildrenOf(job, ct);
        Assert.Equal(2, children.Count);
        Assert.Equal(2, job.TotalChildCount);
        Assert.All(children, c => Assert.Equal(ProcessingJobType.BackupBatch, c.Type));
        Assert.All(children, c => Assert.Equal(ProcessingJobStatus.Pending, c.Status));
    }

    [Fact]
    public async Task HandleAsync_StampsTheSourceEntityOnItselfAndItsChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        Assert.Equal(ProcessingEntityType.BackupSource, job.EntityType);
        Assert.Equal(_sourceId, job.EntityId);

        var children = await ChildrenOf(job, ct);
        Assert.NotEmpty(children);
        Assert.All(children, c => Assert.Equal(ProcessingEntityType.BackupSource, c.EntityType));
        Assert.All(children, c => Assert.Equal(_sourceId, c.EntityId));
        Assert.All(children, c => Assert.Equal(_sourceId, c.GetPayload<BackupBatchPayload>()!.BackupSourceId));
    }

    // Every path the planner emits sits under the source kind's own root folder, so several kinds can
    // share one target without their prunes reaching into each other's files.
    [Fact]
    public async Task HandleAsync_ChildPathsAreNamespacedUnderTheSourceKindsRootFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument("A", new DateOnly(2025, 1, 1));
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        var actions = (await ChildrenOf(job, ct))
            .SelectMany(c => c.GetPayload<BackupBatchPayload>()!.Actions)
            .ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.StartsWith(
            $"{DocumentsBackupSourceDescriptor.RootFolderName}/", a.RelativePath));
    }

    // Negative so uploads and classification preempt the mirror, and descending by index so the worker
    // claims batches in the Delete → Move → Put order the plan builder emitted them in.
    [Fact]
    public async Task HandleAsync_EnqueuesChildrenAtDescendingNegativePriority()
    {
        var ct = TestContext.Current.CancellationToken;
        _options.MaxFilesPerBatch = 1;
        SeedDocument("A", new DateOnly(2025, 1, 1), versionCount: 3);
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        var priorities = (await ChildrenOf(job, ct)).Select(c => c.Priority).OrderByDescending(p => p).ToList();
        Assert.All(priorities, p => Assert.True(p < 0, $"priority {p} was not negative"));
        Assert.Equal(
            [BackupToTargetJobHandler.ChildBasePriority,
             BackupToTargetJobHandler.ChildBasePriority - 1,
             BackupToTargetJobHandler.ChildBasePriority - 2],
            priorities);
    }

    [Fact]
    public async Task HandleAsync_SignalsTheWorkerOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        _signal.Received(1).Signal();
    }

    [Fact]
    public async Task HandleAsync_IdempotentOnReEntry_RestoresTotalChildCountAndSpawnsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();
        var job = CreateJob();

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupBatch,
            Status = ProcessingJobStatus.Completed,
            ParentJobId = job.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupBatch,
            Status = ProcessingJobStatus.Pending,
            ParentJobId = job.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _sut.HandleAsync(job, ct);

        Assert.Equal(2, job.TotalChildCount);
        Assert.Equal(2, (await ChildrenOf(job, ct)).Count);
        _signal.DidNotReceive().Signal();
    }

    [Fact]
    public async Task HandleAsync_TrashedDocumentsStayMirrored()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId = SeedDocument("Trashed", new DateOnly(2025, 1, 1));

        var document = await _db.Documents
            .IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .FirstAsync(d => d.Id == docId, ct);
        document.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.Equal(1, summary!.Puts);
    }

    [Fact]
    public async Task DryRun_WritesTheSummaryAndSpawnsZeroChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument("A", new DateOnly(2025, 1, 1), versionCount: 2);
        SeedDocument("B", documentDate: null);
        var job = CreateJob(dryRun: true);

        await _sut.HandleAsync(job, ct);

        Assert.Empty(await ChildrenOf(job, ct));
        Assert.Equal(0, job.TotalChildCount);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.NotNull(summary);
        Assert.True(summary.DryRun);
        Assert.Equal(_sourceId, summary.BackupSourceId);
        Assert.Equal(3, summary.Puts);
        Assert.Equal(0, summary.BatchCount);
        _signal.DidNotReceive().Signal();
    }

    [Fact]
    public async Task RealRun_WritesTheSummaryToo()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument("A", new DateOnly(2025, 1, 1));
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.NotNull(summary);
        Assert.False(summary.DryRun);
        Assert.Equal(1, summary.Puts);
        Assert.Equal(1, summary.BatchCount);
    }

    // --- The wake matrix -------------------------------------------------------------------------
    //
    // Four ways a run can arrive at a pairing it cannot mirror. Three are operator decisions or expected
    // races and skip quietly; only a broken destination fails loudly.

    // The pairing was deleted while this run sat queued. Also absorbs a legacy payload that named no
    // source at all, which deserializes to Guid.Empty and matches nothing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceRowGone_SkipsQuietly(bool emptyPayloadId)
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        var missingId = emptyPayloadId ? Guid.Empty : Guid.NewGuid();
        var job = CreateJob(sourceId: missingId);

        await _sut.HandleAsync(job, ct);

        Assert.Empty(await ChildrenOf(job, ct));

        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.Skipped);
        Assert.Contains("no longer exists", summary.SkipReason);
    }

    // Disabled is an operator decision, not a fault — a still-armed schedule must not fill the job list
    // with red rows.
    [Fact]
    public async Task DisabledSource_SkipsWithAReasonAndNoChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        var source = await _db.BackupSources.SingleAsync(s => s.Id == _sourceId, ct);
        source.IsEnabled = false;
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.Empty(await ChildrenOf(job, ct));

        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.Skipped);
        Assert.Contains("source is disabled", summary.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    // Pausing the destination has to stop every pairing writing to it, not just decorate the page.
    [Fact]
    public async Task DisabledTarget_SkipsWithAReasonAndNoChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();
        _db.BackupTargetStates.Add(new BackupTargetState
        {
            TargetKey = TargetKey,
            IsEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.Empty(await ChildrenOf(job, ct));

        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.Skipped);
        Assert.Contains("Target is disabled", summary.SkipReason);
    }

    // A misconfigured destination is a config error the operator must fix. It fails loudly rather than
    // completing quietly, because a mirror that reports success while copying nothing is the worst
    // possible outcome for a backup.
    [Fact]
    public async Task UnavailableTarget_FailsLoudly()
    {
        var ct = TestContext.Current.CancellationToken;
        _resolver.Resolve(Arg.Any<string>()).Returns(new StubTarget(TargetKey, available: false));
        SeedDocument();

        var job = CreateJob();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.HandleAsync(job, ct));
        Assert.Contains("unavailable", ex.Message);
    }

    // The enable flags are checked BEFORE the destination, so an operator who has already paused this
    // pairing is not paged about a target they know is broken.
    [Fact]
    public async Task DisabledSourceOnAnUnavailableTarget_SkipsRatherThanThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        _resolver.Resolve(Arg.Any<string>()).Returns(new StubTarget(TargetKey, available: false));
        SeedDocument();

        var source = await _db.BackupSources.SingleAsync(s => s.Id == _sourceId, ct);
        source.IsEnabled = false;
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.True(job.GetResult<BackupRunSummary>()!.Skipped);
    }

    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentRunForTheSameSource_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            EntityType = ProcessingEntityType.BackupSource,
            EntityId = _sourceId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.Empty(await ChildrenOf(job, ct));

        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.Skipped);
        Assert.Contains("already in progress", summary.SkipReason);
    }

    // Children carry their parent's EntityId, so single-flight has to filter them out explicitly —
    // otherwise the second run after any real run would skip forever.
    [Fact]
    public async Task AnInProgressBATCHForTheSameSource_DoesNotBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            EntityType = ProcessingEntityType.BackupSource,
            EntityId = _sourceId,
            ParentJobId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.NotEmpty(await ChildrenOf(job, ct));
    }

    [Fact]
    public async Task AnInProgressRunForADIFFERENTSource_DoesNotBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            EntityType = ProcessingEntityType.BackupSource,
            EntityId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.NotEmpty(await ChildrenOf(job, ct));
    }

    [Fact]
    public async Task BreakerTrip_LatchesOntoTheSourceRow()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedMirroredState(40);
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var source = await ReloadSourceAsync(ct);
        Assert.NotNull(source.PruneBlockedAt);
        Assert.Equal(40, source.PruneBlockedCount);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.PruneBlocked);
        Assert.Equal(40, summary.BlockedDeleteCount);
        Assert.Equal(0, summary.Deletes);
    }

    // A dry run reports what WOULD be blocked but must not persist the operator-facing latch — a preview
    // that leaves a "N deletions were blocked" banner behind has touched something.
    [Fact]
    public async Task DryRunThatWouldTripTheBreaker_ReportsItButDoesNotLatch()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedMirroredState(40);
        await _db.SaveChangesAsync(ct);

        var job = CreateJob(dryRun: true);
        await _sut.HandleAsync(job, ct);

        // The preview still reports the would-be block...
        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.PruneBlocked);
        Assert.Equal(40, summary.BlockedDeleteCount);

        // ...but nothing was persisted.
        Assert.Null((await ReloadSourceAsync(ct)).PruneBlockedAt);
    }

    [Fact]
    public async Task AllowLargePrune_ClearsTheWayForTheDeletes()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedMirroredState(40);
        await _db.SaveChangesAsync(ct);

        var job = CreateJob(allowLargePrune: true);
        await _sut.HandleAsync(job, ct);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.False(summary!.PruneBlocked);
        Assert.Equal(40, summary.Deletes);
        Assert.Null((await ReloadSourceAsync(ct)).PruneBlockedAt);
    }

    // "Prune anyway" is the admin acting ON the warning, so it must take the banner down with it —
    // otherwise the card reports a block that has already been carried out, and the only way to silence
    // it is a second, now meaningless, Dismiss.
    [Fact]
    public async Task AllowLargePrune_ClearsAnExistingLatch()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedMirroredState(40);
        await LatchAsync(blockedCount: 40, ct);

        var job = CreateJob(allowLargePrune: true);
        await _sut.HandleAsync(job, ct);

        var source = await ReloadSourceAsync(ct);
        Assert.Null(source.PruneBlockedAt);
        Assert.Null(source.PruneBlockedCount);
    }

    // The other half of that rule, and the reason the clear is gated on allowLargePrune rather than on
    // "the plan came back unblocked": an ORDINARY run that happens to want fewer deletions must leave the
    // warning up, or it disappears before anyone has seen it.
    [Fact]
    public async Task AnOrdinaryRunWithFewDeletions_LeavesAnExistingLatchAlone()
    {
        var ct = TestContext.Current.CancellationToken;

        // One orphaned state row — well under the breaker's floor, so this run plans a delete and does
        // not trip.
        SeedMirroredState(1);
        await LatchAsync(blockedCount: 40, ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.False(job.GetResult<BackupRunSummary>()!.PruneBlocked);

        var source = await ReloadSourceAsync(ct);
        Assert.NotNull(source.PruneBlockedAt);
        Assert.Equal(40, source.PruneBlockedCount);
    }

    /// <summary>Puts the source in the state a previously-tripped breaker would have left it in.</summary>
    private async Task LatchAsync(int blockedCount, CancellationToken ct)
    {
        var source = await _db.BackupSources.SingleAsync(s => s.Id == _sourceId, ct);
        source.PruneBlockedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        source.PruneBlockedCount = blockedCount;
        await _db.SaveChangesAsync(ct);
    }

    // State rows belong to a pairing, not a target — another pairing's rows must not show up as
    // deletions in this one's plan.
    [Fact]
    public async Task StateRowsOfAnotherSource_AreNotPlannedAsDeletes()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.DocumentBackupStates.Add(new DocumentBackupState
        {
            Id = Guid.NewGuid(),
            BackupSourceId = Guid.NewGuid(),
            DocumentVersionId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            RemoteRelativePath = "Documents/2025/Elsewhere.pdf",
            BackedUpHash = Guid.NewGuid().ToString("N"),
            FileSize = 10,
            BackedUpAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.Equal(0, summary!.Deletes);
        Assert.Equal(0, summary.BlockedDeleteCount);
    }

    [Fact]
    public async Task NothingToDo_SpawnsNoChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        Assert.Empty(await ChildrenOf(job, ct));
        Assert.Equal(0, job.TotalChildCount);
    }

    [Fact]
    public async Task MissingPayload_Throws()
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
    }

    private sealed class StubTarget(string key, bool available) : IBackupTarget
    {
        public string Key => key;
        public BackupTargetKind? Kind => BackupTargetKind.Local;
        public string? Location => "/tmp/mirror";
        public bool IsAvailable => available;
        public string? ConfigurationError => available ? null : "Path is required.";
        public BackupTargetCapabilities Capabilities => BackupTargetCapabilities.Default;

        public Task<string> PutAsync(BackupObjectRef target, Stream content, string contentType, CancellationToken ct) =>
            Task.FromResult(target.RelativePath);

        public Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct) =>
            Task.FromResult(toRelativePath);

        public Task DeleteAsync(BackupObjectRef target, CancellationToken ct) => Task.CompletedTask;
    }
}
