using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
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
    private static readonly Guid TargetEntityId = DeterministicGuid.FromKey(TargetKey);

    private readonly TestAppDbContext _db;
    private readonly IJobSignal _signal;
    private readonly IBackupTargetResolver _resolver;
    private readonly BackupOptions _options = new();
    private readonly BackupToTargetJobHandler _sut;

    public BackupToTargetJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _signal = Substitute.For<IJobSignal>();
        _resolver = Substitute.For<IBackupTargetResolver>();
        _resolver.Resolve(Arg.Any<string>()).Returns(new StubTarget(TargetKey, available: true));

        _sut = new BackupToTargetJobHandler(
            _db, _resolver, Options.Create(_options), _signal,
            NullLogger<BackupToTargetJobHandler>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private ProcessingJob CreateJob(bool dryRun = false, bool allowLargePrune = false, string? targetKey = null)
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        job.SetPayload(new BackupToTargetPayload(targetKey ?? TargetKey, dryRun, allowLargePrune));
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

    private async Task<List<ProcessingJob>> ChildrenOf(ProcessingJob job, CancellationToken ct) =>
        await _db.ProcessingJobs.Where(j => j.ParentJobId == job.Id).ToListAsync(ct);

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
    public async Task HandleAsync_StampsTheTargetEntityOnItselfAndItsChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        Assert.Equal(ProcessingEntityType.BackupTarget, job.EntityType);
        Assert.Equal(TargetEntityId, job.EntityId);

        var children = await ChildrenOf(job, ct);
        Assert.All(children, c => Assert.Equal(TargetEntityId, c.EntityId));
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
        Assert.Equal(1, summary!.PutCount);
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
        Assert.Equal(3, summary.PutCount);
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
        Assert.Equal(1, summary.PutCount);
        Assert.Equal(1, summary.BatchCount);
    }

    // Disabled is an operator decision, not a fault — a still-armed schedule must not fill the job list
    // with red rows.
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
        Assert.Contains("disabled", summary.SkipReason, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task ConcurrentRunForTheSameTarget_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            EntityType = ProcessingEntityType.BackupTarget,
            EntityId = TargetEntityId,
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

    [Fact]
    public async Task AnInProgressRunForADIFFERENTTarget_DoesNotBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.InProgress,
            EntityType = ProcessingEntityType.BackupTarget,
            EntityId = DeterministicGuid.FromKey("some-other-target"),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.NotEmpty(await ChildrenOf(job, ct));
    }

    [Fact]
    public async Task BreakerTrip_LatchesOntoTheTargetStateRow()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 40; i++)
        {
            _db.DocumentBackupStates.Add(new DocumentBackupState
            {
                Id = Guid.NewGuid(),
                TargetKey = TargetKey,
                DocumentVersionId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                RemoteRelativePath = $"2025/Gone{i}.pdf",
                BackedUpHash = Guid.NewGuid().ToString("N"),
                FileSize = 10,
                BackedUpAt = DateTimeOffset.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var state = await _db.BackupTargetStates.SingleAsync(s => s.TargetKey == TargetKey, ct);
        Assert.NotNull(state.PruneBlockedAt);
        Assert.Equal(40, state.PruneBlockedCount);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.PruneBlocked);
        Assert.Equal(40, summary.BlockedDeleteCount);
        Assert.Equal(0, summary.DeleteCount);
    }

    // A dry run reports what WOULD be blocked but must not persist the operator-facing latch — a preview
    // that leaves a "N deletions were blocked" banner behind (and a state row) has touched something.
    [Fact]
    public async Task DryRunThatWouldTripTheBreaker_ReportsItButDoesNotLatch()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 40; i++)
        {
            _db.DocumentBackupStates.Add(new DocumentBackupState
            {
                Id = Guid.NewGuid(),
                TargetKey = TargetKey,
                DocumentVersionId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                RemoteRelativePath = $"2025/Gone{i}.pdf",
                BackedUpHash = Guid.NewGuid().ToString("N"),
                FileSize = 10,
                BackedUpAt = DateTimeOffset.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);

        var job = CreateJob(dryRun: true);
        await _sut.HandleAsync(job, ct);

        // The preview still reports the would-be block...
        var summary = job.GetResult<BackupRunSummary>();
        Assert.True(summary!.PruneBlocked);
        Assert.Equal(40, summary.BlockedDeleteCount);

        // ...but nothing was persisted: no latch, and no state row created for it.
        Assert.Null(await _db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == TargetKey, ct));
    }

    [Fact]
    public async Task AllowLargePrune_ClearsTheWayForTheDeletes()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 40; i++)
        {
            _db.DocumentBackupStates.Add(new DocumentBackupState
            {
                Id = Guid.NewGuid(),
                TargetKey = TargetKey,
                DocumentVersionId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                RemoteRelativePath = $"2025/Gone{i}.pdf",
                BackedUpHash = Guid.NewGuid().ToString("N"),
                FileSize = 10,
                BackedUpAt = DateTimeOffset.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);

        var job = CreateJob(allowLargePrune: true);
        await _sut.HandleAsync(job, ct);

        var summary = job.GetResult<BackupRunSummary>();
        Assert.False(summary!.PruneBlocked);
        Assert.Equal(40, summary.DeleteCount);
        Assert.Null(await _db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == TargetKey, ct));
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
