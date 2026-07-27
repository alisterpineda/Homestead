using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class BackupBatchJobHandlerTests : IDisposable
{
    private const string TargetKey = "local";

    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly RecordingTarget _target;
    private readonly IProcessingNotifier _notifier;
    private readonly IJobSignal _signal;
    private readonly IBackupTargetGate _gate;
    private readonly FakeTimeProvider _timeProvider;
    private readonly BackupBatchJobHandler _sut;

    /// <summary>The pairing every batch in these tests belongs to, seeded by the constructor.</summary>
    private readonly Guid _sourceId = Guid.NewGuid();

    public BackupBatchJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), "homestead-backup-batch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mediaPath);

        _target = new RecordingTarget(TargetKey);
        _notifier = Substitute.For<IProcessingNotifier>();
        _signal = Substitute.For<IJobSignal>();
        _timeProvider = new FakeTimeProvider();

        var resolver = Substitute.For<IBackupTargetResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(_target);

        // Passes through to the target's own config verdict, which is what the local-target tests here
        // are asserting on; the Drive-specific composition is covered in BackupTargetGateTests.
        _gate = Substitute.For<IBackupTargetGate>();
        _gate.CheckAsync(Arg.Any<IBackupTarget>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var t = call.Arg<IBackupTarget>();
                return Task.FromResult(new TargetGateResult(t.IsAvailable, t.ConfigurationError));
            });

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

        _sut = new BackupBatchJobHandler(
            _db, resolver, _gate,
            Options.Create(new FileStorageOptions { MediaPath = _mediaPath }),
            Options.Create(new JobProcessingOptions()),
            _notifier,
            _signal,
            _timeProvider,
            NullLogger<BackupBatchJobHandler>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_mediaPath, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a blob into the fake media root and returns its (relative path, real hash).</summary>
    private (string RelativePath, string Hash) SeedBlob(string content)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var relativePath = Homestead.WebApi.Domain.Documents.DocumentVersion.GetFilePath(hash, ".pdf");
        var absolute = Path.Combine(_mediaPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);

        return (relativePath, hash);
    }

    private ProcessingJob CreateJob(
        IEnumerable<BackupAction> actions, Guid? parentJobId = null, Guid? sourceId = null)
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupBatch,
            Status = ProcessingJobStatus.InProgress,
            EntityType = ProcessingEntityType.BackupSource,
            EntityId = sourceId ?? _sourceId,
            CreatedByUserId = Guid.NewGuid(),
            ParentJobId = parentJobId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        job.SetPayload(new BackupBatchPayload(sourceId ?? _sourceId, [.. actions]));
        return job;
    }

    private static BackupAction Put(Guid versionId, string path, string sourcePath, string hash, long size = 5) => new()
    {
        Kind = BackupActionKind.Put,
        VersionId = versionId,
        DocumentId = Guid.NewGuid(),
        RelativePath = path,
        SourceFilePath = sourcePath,
        FileHash = hash,
        FileSize = size,
    };

    private DocumentBackupState SeedState(Guid versionId, string path, string hash = "hash")
    {
        var state = new DocumentBackupState
        {
            Id = Guid.NewGuid(),
            BackupSourceId = _sourceId,
            DocumentVersionId = versionId,
            DocumentId = Guid.NewGuid(),
            RemoteRelativePath = path,
            RemoteHandle = path,
            BackedUpHash = hash,
            FileSize = 5,
            BackedUpAt = DateTimeOffset.UtcNow,
        };
        _db.DocumentBackupStates.Add(state);
        _db.SaveChanges();
        return state;
    }

    [Fact]
    public async Task Put_CopiesTheBlobAndWritesTheStateRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        var job = CreateJob([Put(versionId, "2025/Doc.pdf", sourcePath, hash)]);
        await _sut.HandleAsync(job, ct);

        Assert.Equal("hello", _target.Objects["2025/Doc.pdf"]);

        var state = await _db.DocumentBackupStates.SingleAsync(s => s.DocumentVersionId == versionId, ct);
        Assert.Equal("2025/Doc.pdf", state.RemoteRelativePath);
        Assert.Equal("2025/Doc.pdf", state.RemoteHandle);
        Assert.Equal(hash, state.BackedUpHash);
    }

    // Ordering is the idempotency guarantee: a crash between the write and the state row re-copies next
    // run (harmless, the path is stable), whereas a state row written first would claim a copy that isn't
    // there and the mirror would never heal.
    [Fact]
    public async Task Put_WritesNoStateRowWhenTheTargetWriteFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();
        _target.FailPutsAt = "2025/Doc.pdf";

        var job = CreateJob([Put(versionId, "2025/Doc.pdf", sourcePath, hash)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));
    }

    // Real corruption. The mirror is the copy of last resort, so bad bytes must not be propagated over a
    // good backup.
    [Fact]
    public async Task Put_HashMismatch_FailsThatActionAndCopiesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, _) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        var job = CreateJob([Put(versionId, "2025/Doc.pdf", sourcePath, hash: "not-the-real-hash")]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        Assert.Contains("hash mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_target.Objects);
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));
    }

    // A version can be permanently deleted between the parent's plan and this batch running. Expected
    // race, not a fault — and because the version row is gone, it is never re-planned.
    [Fact]
    public async Task Put_MissingSourceFileForADeletedVersion_IsSkippedWithoutFailingTheBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var (goodSource, goodHash) = SeedBlob("survivor");
        var missingVersion = Guid.NewGuid();
        var goodVersion = Guid.NewGuid();

        var job = CreateJob(
        [
            Put(missingVersion, "2025/Vanished.pdf", "documents/files/dead/deadbeef.pdf", "deadbeef"),
            Put(goodVersion, "2025/Survivor.pdf", goodSource, goodHash),
        ]);

        await _sut.HandleAsync(job, ct);

        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == missingVersion, ct));
        Assert.True(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == goodVersion, ct));
        Assert.Single(_target.Objects);
    }

    // The other half of that distinction: the version row is still there but its blob is not, so the
    // library has lost a file. Skipping silently would write no state row and re-plan the same copy on
    // every future run — the mirror could never reach a steady state, while every run still reported a
    // copy count that never happened.
    [Fact]
    public async Task Put_MissingSourceFileForALiveVersion_FailsInsteadOfSkipping()
    {
        var ct = TestContext.Current.CancellationToken;
        var versionId = Guid.NewGuid();

        var documentId = Guid.NewGuid();
        _db.Documents.Add(new Homestead.WebApi.Domain.Documents.Document
        {
            Id = documentId,
            Name = "Orphaned",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.DocumentVersions.Add(new Homestead.WebApi.Domain.Documents.DocumentVersion
        {
            Id = versionId,
            DocumentId = documentId,
            VersionNumber = 1,
            ContentType = "application/pdf",
            FileSize = 5,
            FilePath = "documents/files/dead/deadbeef.pdf",
            FileHash = "deadbeef",
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var job = CreateJob(
            [Put(versionId, "2025/Orphaned.pdf", "documents/files/dead/deadbeef.pdf", "deadbeef")]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        Assert.Contains("missing from storage", ex.Message);
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));
    }

    // One bad file must not cost the other ninety-nine their state rows — that is what would turn a
    // transient error into a permanent re-copy loop.
    [Fact]
    public async Task OneFailingAction_StillPersistsEverythingElseThenThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (goodSource, goodHash) = SeedBlob("good");
        var (badSource, _) = SeedBlob("bad");
        var goodVersion = Guid.NewGuid();
        var badVersion = Guid.NewGuid();

        var job = CreateJob(
        [
            Put(badVersion, "2025/Bad.pdf", badSource, hash: "wrong-hash"),
            Put(goodVersion, "2025/Good.pdf", goodSource, goodHash),
        ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        Assert.Contains("1 of 2", ex.Message);
        Assert.True(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == goodVersion, ct));
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == badVersion, ct));
    }

    [Fact]
    public async Task Move_RelocatesTheObjectAndUpdatesTheStateRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var versionId = Guid.NewGuid();
        SeedState(versionId, "2025/Doc.pdf");
        _target.Objects["2025/Doc.pdf"] = "payload";

        var job = CreateJob(
        [
            new BackupAction
            {
                Kind = BackupActionKind.Move,
                VersionId = versionId,
                RelativePath = "2026/Renamed.pdf",
                FromRelativePath = "2025/Doc.pdf",
                Handle = "2025/Doc.pdf",
                FileHash = "hash",
            }
        ]);

        await _sut.HandleAsync(job, ct);

        Assert.False(_target.Objects.ContainsKey("2025/Doc.pdf"));
        Assert.Equal("payload", _target.Objects["2026/Renamed.pdf"]);

        var state = await _db.DocumentBackupStates.SingleAsync(s => s.DocumentVersionId == versionId, ct);
        Assert.Equal("2026/Renamed.pdf", state.RemoteRelativePath);
        Assert.Equal("2026/Renamed.pdf", state.RemoteHandle);
    }

    // Self-healing: with no state row the next run's diff sees "never copied" and re-puts it from scratch.
    [Fact]
    public async Task Move_MissingRemoteSource_DropsTheStateRowInsteadOfFailing()
    {
        var ct = TestContext.Current.CancellationToken;
        var versionId = Guid.NewGuid();
        SeedState(versionId, "2025/Doc.pdf");

        var job = CreateJob(
        [
            new BackupAction
            {
                Kind = BackupActionKind.Move,
                VersionId = versionId,
                RelativePath = "2026/Renamed.pdf",
                FromRelativePath = "2025/Doc.pdf",
                FileHash = "hash",
            }
        ]);

        await _sut.HandleAsync(job, ct);

        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));
    }

    [Fact]
    public async Task Delete_RemovesTheObjectAndTheStateRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var versionId = Guid.NewGuid();
        SeedState(versionId, "2025/Gone.pdf");
        _target.Objects["2025/Gone.pdf"] = "payload";

        var job = CreateJob(
        [
            new BackupAction
            {
                Kind = BackupActionKind.Delete,
                VersionId = versionId,
                RelativePath = "2025/Gone.pdf",
                Handle = "2025/Gone.pdf",
                FileHash = "hash",
            }
        ]);

        await _sut.HandleAsync(job, ct);

        Assert.Empty(_target.Objects);
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));
    }

    [Fact]
    public async Task Put_OverAnExistingStateRow_UpdatesItInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("v2 content");
        var versionId = Guid.NewGuid();
        SeedState(versionId, "2025/Doc.pdf", hash: "old-hash");

        var job = CreateJob([Put(versionId, "2025/Doc.pdf", sourcePath, hash)]);
        await _sut.HandleAsync(job, ct);

        var state = await _db.DocumentBackupStates.SingleAsync(s => s.DocumentVersionId == versionId, ct);
        Assert.Equal(hash, state.BackedUpHash);
        Assert.Single(_db.DocumentBackupStates.Where(s => s.DocumentVersionId == versionId));
    }

    // The established child-notification convention: the user sees only the rolled-up parent result.
    [Fact]
    public async Task AsAChild_SuppressesItsOwnNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        var job = CreateJob([Put(Guid.NewGuid(), "2025/Doc.pdf", sourcePath, hash)], parentJobId: Guid.NewGuid());
        await _sut.HandleAsync(job, ct);

        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(), Arg.Any<ProcessingEntityType?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<Dictionary<string, string>?>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task WithoutAParent_ItDoesNotify()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        var job = CreateJob([Put(Guid.NewGuid(), "2025/Doc.pdf", sourcePath, hash)]);
        await _sut.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id, ProcessingJobStatus.Completed, ProcessingEntityType.BackupSource,
            Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Is<string?>(error => error == null),
            job.CreatedByUserId,
            Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<Dictionary<string, string>?>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>());
    }

    // The enqueue-vs-delete race: the pairing (and its state rows) can be deleted while this batch sits
    // queued. Writing state for a source that no longer exists would violate the FK and fail the batch
    // loudly for something that is not a fault, so the handler returns quietly instead.
    [Fact]
    public async Task SourceRowGone_ReturnsQuietlyWithoutTouchingTheTarget()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        var job = CreateJob(
            [Put(versionId, "Documents/2025/Doc.pdf", sourcePath, hash)],
            sourceId: Guid.NewGuid());

        await _sut.HandleAsync(job, ct);

        Assert.Empty(_target.Objects);
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));
    }

    [Fact]
    public async Task UnavailableTarget_FailsTheBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        _target.Available = false;

        var job = CreateJob([Put(Guid.NewGuid(), "2025/Doc.pdf", "documents/files/aa/aa.pdf", "aa")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));
    }

    [Fact]
    public async Task MissingPayload_Throws()
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupBatch,
            Status = ProcessingJobStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
    }

    // --- Deadline and continuations -------------------------------------------------------------------
    //
    // These cover the parentless shape, where the continuation is a plain insert. The atomic
    // bump-then-insert against a live PARENT is exercised in the integration suite instead: it turns on
    // ExecuteUpdate's database-side increment, which is what makes the count safe under concurrency and
    // which the InMemory provider does not implement.

    /// <summary>A job whose budget is already spent, so the handler must defer rather than start work.</summary>
    private ProcessingJob CreateExhaustedJob(IEnumerable<BackupAction> actions, Guid? parentJobId = null)
    {
        var job = CreateJob(actions, parentJobId);

        // Started long enough ago that the five-minute budget (less the 30 s margin) is gone.
        job.StartedAt = _timeProvider.GetUtcNow().AddMinutes(-10);
        return job;
    }

    private Task<ProcessingJob> ContinuationOf(ProcessingJob job, CancellationToken ct) =>
        _db.ProcessingJobs.SingleAsync(
            j => j.Type == ProcessingJobType.BackupBatch && j.Id != job.Id, ct);

    // A large change set has to converge over a chain of GREEN batches — a serial procession of Failed
    // runs makes a working backup read as broken.
    [Fact]
    public async Task PastTheDeadline_DefersTheRemainderAndCompletesSuccessfully()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        var job = CreateExhaustedJob(
            [Put(Guid.NewGuid(), "2025/A.pdf", sourcePath, hash),
             Put(Guid.NewGuid(), "2025/B.pdf", sourcePath, hash)]);

        // No throw: running out of budget is pacing, not failure.
        await _sut.HandleAsync(job, ct);

        Assert.Empty(_target.Objects);

        var payload = (await ContinuationOf(job, ct)).GetPayload<BackupBatchPayload>()!;
        Assert.Equal(2, payload.Actions.Count);
        Assert.Equal(["2025/A.pdf", "2025/B.pdf"], payload.Actions.Select(a => a.RelativePath));

        _signal.Received().Signal();
    }

    // Only the UNDONE actions are deferred; the ones that landed must not be redone.
    [Fact]
    public async Task WithBudgetLeft_NothingIsDeferred()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        var job = CreateJob([Put(Guid.NewGuid(), "2025/A.pdf", sourcePath, hash)]);
        await _sut.HandleAsync(job, ct);

        Assert.Single(_target.Objects);
        Assert.False(await _db.ProcessingJobs.AnyAsync(j => j.Id != job.Id, ct));
    }

    // Claim order is priority then CreatedAt, so an EQUAL priority keeps the continuation directly behind
    // its predecessor — and, crucially, still ahead of the run's final prune batch.
    [Fact]
    public async Task AContinuation_InheritsItsPredecessorsPriorityAndProvenance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        var job = CreateExhaustedJob([Put(Guid.NewGuid(), "2025/A.pdf", sourcePath, hash)]);
        job.Priority = -104;

        await _sut.HandleAsync(job, ct);

        var continuation = await ContinuationOf(job, ct);

        Assert.Equal(-104, continuation.Priority);
        Assert.Equal(job.EntityId, continuation.EntityId);
        Assert.Equal(job.CreatedByUserId, continuation.CreatedByUserId);
        Assert.Equal(ProcessingJobStatus.Pending, continuation.Status);
    }

    // --- Suspended uploads ----------------------------------------------------------------------------

    // Without this, a file larger than one budget window could NEVER be mirrored — every attempt would
    // restart from zero and be cut off at the same place, forever.
    [Fact]
    public async Task ASuspendedPut_ParksTheSessionAndDefersTheAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        _target.SuspendPutsAt["2025/Big.pdf"] = new BackupPutResult.Suspended("https://session/1", 4096);

        var job = CreateJob([Put(versionId, "2025/Big.pdf", sourcePath, hash)]);
        await _sut.HandleAsync(job, ct);

        var session = await _db.BackupUploadSessions.SingleAsync(s => s.DocumentVersionId == versionId, ct);
        Assert.Equal("https://session/1", session.SessionUri);
        Assert.Equal(4096, session.BytesCommitted);
        Assert.Equal(hash, session.FileHash);

        // Nothing is claimed as mirrored — the absence of a state row until success is load-bearing.
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == versionId, ct));

        Assert.True(await _db.ProcessingJobs.AnyAsync(j => j.Id != job.Id, ct));
    }

    [Fact]
    public async Task AParkedSession_IsHandedBackToTheTargetToResume()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        _db.BackupUploadSessions.Add(new BackupUploadSession
        {
            Id = Guid.NewGuid(),
            BackupSourceId = _sourceId,
            DocumentVersionId = versionId,
            SessionUri = "https://session/1",
            BytesCommitted = 4096,
            FileHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _sut.HandleAsync(CreateJob([Put(versionId, "2025/Big.pdf", sourcePath, hash)]), ct);

        var context = Assert.Single(_target.PutContexts);
        Assert.Equal("https://session/1", context.ResumeSessionUri);
        Assert.Equal(4096, context.ResumeOffset);
    }

    [Fact]
    public async Task ACompletedPut_ClearsItsParkedSession()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        _db.BackupUploadSessions.Add(new BackupUploadSession
        {
            Id = Guid.NewGuid(),
            BackupSourceId = _sourceId,
            DocumentVersionId = versionId,
            SessionUri = "https://session/1",
            BytesCommitted = 4096,
            FileHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _sut.HandleAsync(CreateJob([Put(versionId, "2025/Big.pdf", sourcePath, hash)]), ct);

        Assert.False(await _db.BackupUploadSessions.AnyAsync(ct));
    }

    // Resuming a session whose content has changed would commit the tail of the new file on top of the
    // head of the old one.
    [Fact]
    public async Task ASessionWhoseContentChanged_IsDiscardedRatherThanResumed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var versionId = Guid.NewGuid();

        _db.BackupUploadSessions.Add(new BackupUploadSession
        {
            Id = Guid.NewGuid(),
            BackupSourceId = _sourceId,
            DocumentVersionId = versionId,
            SessionUri = "https://session/1",
            BytesCommitted = 4096,
            FileHash = "a-completely-different-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _sut.HandleAsync(CreateJob([Put(versionId, "2025/Big.pdf", sourcePath, hash)]), ct);

        var context = Assert.Single(_target.PutContexts);
        Assert.Null(context.ResumeSessionUri);
        Assert.False(await _db.BackupUploadSessions.AnyAsync(ct));
    }

    [Fact]
    public async Task APut_CarriesBothHashesSoTheTargetCanVerifyWhicheverItReports()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        _target.ContentHashSupported = true;

        await _sut.HandleAsync(CreateJob([Put(Guid.NewGuid(), "2025/Doc.pdf", sourcePath, hash)]), ct);

        var context = Assert.Single(_target.PutContexts);
        Assert.Equal(hash, context.Sha256Hex);
        Assert.False(string.IsNullOrEmpty(context.Md5Hex));
    }

    // MD5 is a fallback for destinations that report their own checksum; computing it for a filesystem
    // that reports none is pure waste on every byte of the library.
    [Fact]
    public async Task APut_SkipsTheMd5PassForATargetThatReportsNoChecksum()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        await _sut.HandleAsync(CreateJob([Put(Guid.NewGuid(), "2025/Doc.pdf", sourcePath, hash)]), ct);

        Assert.Null(Assert.Single(_target.PutContexts).Md5Hex);
    }

    // --- Out of space ---------------------------------------------------------------------------------

    // Remaining puts cannot succeed, but moves and deletes still run — deletes are exactly what frees
    // space.
    [Fact]
    public async Task OutOfSpace_SkipsRemainingPutsButStillRunsDeletes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        var deletedVersion = Guid.NewGuid();
        SeedState(deletedVersion, "2025/Old.pdf");
        _target.Objects["2025/Old.pdf"] = "old";

        _target.OutOfSpaceAt = "2025/A.pdf";

        var job = CreateJob([
            Put(Guid.NewGuid(), "2025/A.pdf", sourcePath, hash),
            Put(Guid.NewGuid(), "2025/B.pdf", sourcePath, hash),
            new BackupAction
            {
                Kind = BackupActionKind.Delete,
                VersionId = deletedVersion,
                RelativePath = "2025/Old.pdf",
                Handle = "2025/Old.pdf",
            },
        ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        // One clean sentence, not two hundred identical fragments.
        Assert.Contains("out of storage space", ex.Message);
        Assert.DoesNotContain(";", ex.Message);

        Assert.DoesNotContain("2025/B.pdf", _target.Objects.Keys);
        Assert.DoesNotContain("2025/Old.pdf", _target.Objects.Keys);
        Assert.False(await _db.DocumentBackupStates.AnyAsync(s => s.DocumentVersionId == deletedVersion, ct));
    }

    // A typed marker, because the card must decide how to render without parsing message strings — and a
    // parent job carries no error string of its own.
    [Fact]
    public async Task OutOfSpace_StagesACodedOutcomeOnTheJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");
        _target.OutOfSpaceAt = "2025/A.pdf";

        var job = CreateJob([
            Put(Guid.NewGuid(), "2025/A.pdf", sourcePath, hash),
            Put(Guid.NewGuid(), "2025/B.pdf", sourcePath, hash),
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        var outcome = JsonSerializer.Deserialize<BackupBatchOutcome>(job.Result!)!;
        Assert.Equal(BackupBatchOutcome.OutOfSpaceCode, outcome.ErrorCode);
        Assert.Equal(2, outcome.SkippedPuts);
    }

    // --- Prune dispatch -------------------------------------------------------------------------------

    [Fact]
    public async Task PruneFolderActions_AreDispatchedAsOneCallAndNeverCountAsFailures()
    {
        var ct = TestContext.Current.CancellationToken;

        var job = CreateJob([
            new BackupAction { Kind = BackupActionKind.PruneFolder, RelativePath = "Documents/2025/_versions" },
            new BackupAction { Kind = BackupActionKind.PruneFolder, RelativePath = "Documents/2025" },
        ]);

        await _sut.HandleAsync(job, ct);

        Assert.Equal(["Documents/2025/_versions", "Documents/2025"], _target.PrunedFolders);
        Assert.Equal(1, _target.PruneCallCount);
    }

    // Best-effort by design: failing the run's LAST batch over housekeeping would paint a fully-converged
    // mirror red.
    [Fact]
    public async Task AFailingPrune_DoesNotFailTheBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        _target.FailPrune = true;

        var job = CreateJob([
            new BackupAction { Kind = BackupActionKind.PruneFolder, RelativePath = "Documents/2025" },
        ]);

        await _sut.HandleAsync(job, ct);
    }

    // --- Fail-fast off the latch ----------------------------------------------------------------------

    // Every still-queued sibling batch of a run whose credential died mid-flight short-circuits off the
    // persisted marker instead of hammering the token endpoint in turn.
    [Fact]
    public async Task AGateRefusal_FailsTheBatchBeforeTouchingTheTarget()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sourcePath, hash) = SeedBlob("hello");

        _gate.CheckAsync(Arg.Any<IBackupTarget>(), Arg.Any<CancellationToken>())
            .Returns(new TargetGateResult(false, "Google revoked the authorization — reconnect."));

        var job = CreateJob([Put(Guid.NewGuid(), "2025/Doc.pdf", sourcePath, hash)]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));

        Assert.Contains("reconnect", ex.Message);
        Assert.Empty(_target.Objects);
    }

    /// <summary>An in-memory stand-in for a destination, so the handler's contract is what's under test.</summary>
    private sealed class RecordingTarget(string key) : IBackupTarget
    {
        public Dictionary<string, string> Objects { get; } = new(StringComparer.Ordinal);
        public bool Available { get; set; } = true;
        public string? FailPutsAt { get; set; }
        public string? OutOfSpaceAt { get; set; }
        public bool ContentHashSupported { get; set; }
        public bool FailPrune { get; set; }
        public int PruneCallCount { get; private set; }

        public string Key => key;
        public BackupTargetKind? Kind => BackupTargetKind.Local;
        public string? Location => "/tmp/mirror";
        public bool IsAvailable => Available;
        public string? ConfigurationError => Available ? null : "Path is required.";
        public BackupTargetCapabilities Capabilities =>
            BackupTargetCapabilities.Default with { SupportsContentHash = ContentHashSupported };

        /// <summary>Paths whose put should park mid-upload instead of completing.</summary>
        public Dictionary<string, BackupPutResult.Suspended> SuspendPutsAt { get; } =
            new(StringComparer.Ordinal);

        public List<BackupPutContext> PutContexts { get; } = [];
        public List<string> PrunedFolders { get; } = [];
        public List<string> EnsuredFolders { get; } = [];

        public async Task<BackupPutResult> PutAsync(
            BackupObjectRef target, Stream content, BackupPutContext context, CancellationToken ct)
        {
            PutContexts.Add(context);

            if (FailPutsAt == target.RelativePath)
                throw new InvalidOperationException($"Simulated write failure at {target.RelativePath}.");

            if (OutOfSpaceAt == target.RelativePath)
                throw new BackupTargetOutOfSpaceException("Simulated: the target is out of storage space.");

            if (SuspendPutsAt.TryGetValue(target.RelativePath, out var suspended))
                return suspended;

            using var reader = new StreamReader(content);
            Objects[target.RelativePath] = await reader.ReadToEndAsync(ct);
            return new BackupPutResult.Completed(target.RelativePath);
        }

        public Task EnsureFoldersAsync(IReadOnlyCollection<string> folderPaths, CancellationToken ct)
        {
            EnsuredFolders.AddRange(folderPaths);
            return Task.CompletedTask;
        }

        public Task PruneEmptyFoldersAsync(IReadOnlyCollection<string> folderPaths, CancellationToken ct)
        {
            PruneCallCount++;

            if (FailPrune)
                throw new InvalidOperationException("Simulated prune failure.");

            PrunedFolders.AddRange(folderPaths);
            return Task.CompletedTask;
        }

        public Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct)
        {
            var source = from.Handle ?? from.RelativePath;

            if (!Objects.Remove(source, out var payload))
                throw new FileNotFoundException($"'{source}' is missing from target '{key}'.");

            Objects[toRelativePath] = payload;
            return Task.FromResult(toRelativePath);
        }

        public Task DeleteAsync(BackupObjectRef target, CancellationToken ct)
        {
            Objects.Remove(target.Handle ?? target.RelativePath);
            return Task.CompletedTask;
        }
    }
}
