using System.Security.Cryptography;
using System.Text;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        var resolver = Substitute.For<IBackupTargetResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(_target);

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
            _db, resolver,
            Options.Create(new FileStorageOptions { MediaPath = _mediaPath }),
            _notifier,
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

    /// <summary>An in-memory stand-in for a destination, so the handler's contract is what's under test.</summary>
    private sealed class RecordingTarget(string key) : IBackupTarget
    {
        public Dictionary<string, string> Objects { get; } = new(StringComparer.Ordinal);
        public bool Available { get; set; } = true;
        public string? FailPutsAt { get; set; }

        public string Key => key;
        public BackupTargetKind? Kind => BackupTargetKind.Local;
        public string? Location => "/tmp/mirror";
        public bool IsAvailable => Available;
        public string? ConfigurationError => Available ? null : "Path is required.";
        public BackupTargetCapabilities Capabilities => BackupTargetCapabilities.Default;

        public async Task<string> PutAsync(
            BackupObjectRef target, Stream content, string contentType, CancellationToken ct)
        {
            if (FailPutsAt == target.RelativePath)
                throw new InvalidOperationException($"Simulated write failure at {target.RelativePath}.");

            using var reader = new StreamReader(content);
            Objects[target.RelativePath] = await reader.ReadToEndAsync(ct);
            return target.RelativePath;
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
