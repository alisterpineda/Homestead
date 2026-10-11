using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Admin.Features.Backup.GetDocumentBackupState;
using Homestead.WebApi.Modules.Documents.Backup;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin.Backup;

/// <summary>
/// The per-document mirror panel's backend. The cases here are the ones where a plausible-looking
/// implementation would say something untrue: a tombstone counted as a copy, a copy record rendered
/// against a destination nobody can reach, a failure attributed to the wrong version.
/// </summary>
public class GetDocumentBackupStateHandlerTests : IDisposable
{
    private const string TargetKey = "local";

    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly FakeBackupTarget _target = new(TargetKey);
    private readonly IBackupTargetResolver _resolver = Substitute.For<IBackupTargetResolver>();
    private readonly IBackupTargetGate _gate = Substitute.For<IBackupTargetGate>();
    private readonly IBackupSourceCatalog _catalog = Substitute.For<IBackupSourceCatalog>();
    private readonly IBackupRunHistory _runHistory = Substitute.For<IBackupRunHistory>();

    private readonly Guid _documentId = Guid.NewGuid();

    public GetDocumentBackupStateHandlerTests()
    {
        _resolver.Resolve(Arg.Any<string>()).Returns(_target);

        // Passes through the fake's own verdict, so a test flips availability by configuring the target.
        _gate.CheckAsync(Arg.Any<IBackupTarget>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var target = call.Arg<IBackupTarget>();
                return Task.FromResult(new TargetGateResult(target.IsAvailable, target.ConfigurationError));
            });

        _catalog.Find(DocumentsBackupSourceDescriptor.SourceKey)
            .Returns(new DocumentsBackupSourceDescriptor());

        _runHistory.GetLastRunsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _runHistory.GetLastSuccessfulRunsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _runHistory.GetSchedulesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _runHistory.GetActionFailuresAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private GetDocumentBackupStateHandler CreateSut() =>
        new(_db, _resolver, _gate, _catalog, _runHistory);

    private Task<GetDocumentBackupStateResponse?> HandleAsync(Guid? documentId = null) =>
        CreateSut().HandleAsync(
            documentId ?? _documentId,
            TestContext.Current.CancellationToken);

    // --- seeding --------------------------------------------------------------------------------------

    private void SeedDocument()
    {
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Lease",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private Guid SeedVersion(int versionNumber)
    {
        var id = Guid.NewGuid();

        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = id,
            DocumentId = _documentId,
            VersionNumber = versionNumber,
            ContentType = "application/pdf",
            FileSize = 10,
            FilePath = $"documents/files/aa/{id:N}.pdf",
            FileHash = id.ToString("N"),
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        return id;
    }

    private Guid SeedSource(Guid? scheduleId = null)
    {
        var id = Guid.NewGuid();

        _db.BackupSources.Add(new BackupSource
        {
            Id = id,
            SourceKind = DocumentsBackupSourceDescriptor.SourceKey,
            TargetKey = TargetKey,
            IsEnabled = true,
            ScheduleId = scheduleId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        return id;
    }

    private void SeedState(
        Guid sourceId, Guid versionId, string path, string? handle = null, Guid? documentId = null)
    {
        _db.DocumentBackupStates.Add(new DocumentBackupState
        {
            Id = Guid.NewGuid(),
            BackupSourceId = sourceId,
            DocumentVersionId = versionId,
            DocumentId = documentId ?? _documentId,
            RemoteRelativePath = path,
            RemoteHandle = handle ?? path,
            BackedUpHash = "hash",
            FileSize = 4096,
            BackedUpAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    // --- tests ----------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUnknownDocument_IsNull()
    {
        Assert.Null(await HandleAsync(Guid.NewGuid()));
    }

    // Nothing configured to back documents up is not "not backed up" with a warning — the client renders
    // no section at all, because there is nothing for a reader to act on.
    [Fact]
    public async Task WithNoPairings_ReportsTheVersionsAndNoSources()
    {
        SeedDocument();
        SeedVersion(1);

        var response = await HandleAsync();

        Assert.NotNull(response);
        Assert.Equal(_documentId, response.DocumentId);
        Assert.Equal(1, response.LiveVersionCount);
        Assert.Empty(response.Sources);
    }

    [Fact]
    public async Task ACopiedVersion_ReportsWhereItWentAndWhenItWasWritten()
    {
        SeedDocument();
        var versionId = SeedVersion(1);
        var sourceId = SeedSource();
        SeedState(sourceId, versionId, "Documents/2025/Lease.pdf");

        var response = await HandleAsync();

        var source = Assert.Single(response!.Sources);
        Assert.Equal(sourceId, source.BackupSourceId);
        Assert.Equal(TargetKey, source.TargetKey);
        Assert.Equal("Documents", source.SourceDisplayName);
        Assert.True(source.IsAvailable);
        Assert.True(source.IsEnabled);
        Assert.True(source.TargetEnabled);
        Assert.Equal(1, source.CopiedVersionCount);
        Assert.Equal(0, source.PendingRemovalCount);

        var version = Assert.Single(source.Versions);
        Assert.Equal(versionId, version.DocumentVersionId);
        Assert.Equal("Documents/2025/Lease.pdf", version.RemoteRelativePath);
        Assert.NotNull(version.LastWrittenAt);
        Assert.Equal(4096, version.FileSize);
        Assert.Null(version.LastFailureMessage);
    }

    // The version is listed either way. A missing row is exactly the case the panel exists to make
    // visible, so it must never be filtered out into a shorter, healthier-looking list.
    [Fact]
    public async Task AnUncopiedVersion_IsStillListedWithNothingRecorded()
    {
        SeedDocument();
        var versionId = SeedVersion(1);
        SeedSource();

        var response = await HandleAsync();

        var source = Assert.Single(response!.Sources);
        Assert.Equal(0, source.CopiedVersionCount);

        var version = Assert.Single(source.Versions);
        Assert.Equal(versionId, version.DocumentVersionId);
        Assert.Null(version.LastWrittenAt);
        Assert.Null(version.RemoteRelativePath);
        Assert.Null(version.FileSize);
    }

    // State rows are FK-less tombstones that outlive the version they describe — a plain count of them
    // would report "3 of 2 versions copied", and the leftover file really is still sitting in the mirror.
    [Fact]
    public async Task ATombstoneRow_CountsAsPendingRemovalRatherThanAsACopy()
    {
        SeedDocument();
        var liveVersion = SeedVersion(2);
        var sourceId = SeedSource();

        SeedState(sourceId, liveVersion, "Documents/2025/Lease.pdf");
        SeedState(sourceId, Guid.NewGuid(), "Documents/2025/_versions/Lease (v1).pdf");

        var response = await HandleAsync();

        var source = Assert.Single(response!.Sources);
        Assert.Equal(1, source.CopiedVersionCount);
        Assert.Equal(1, source.PendingRemovalCount);
        Assert.Equal(1, response.LiveVersionCount);
        Assert.Single(source.Versions);
    }

    // Disconnecting a target deliberately KEEPS its state rows, so without the gate verdict every
    // document would render a cheerful copy record against a destination nobody can reach.
    [Fact]
    public async Task AnUnavailablePairing_SaysWhyAndSuppressesTheLink()
    {
        SeedDocument();
        var versionId = SeedVersion(1);
        var sourceId = SeedSource();
        SeedState(sourceId, versionId, "Documents/2025/Lease.pdf", handle: "drive-file-id");

        _target.ObjectUrlPrefix = "https://drive.google.com/file/d/";
        _target.Available = false;

        var response = await HandleAsync();

        var source = Assert.Single(response!.Sources);
        Assert.False(source.IsAvailable);
        Assert.Equal("Simulated: this target cannot run.", source.UnavailableReason);

        // The copy record itself is still reported — it is a true record of what was written. Only the
        // link goes, because it would point at something the reader cannot open.
        var version = Assert.Single(source.Versions);
        Assert.Equal("Documents/2025/Lease.pdf", version.RemoteRelativePath);
        Assert.Null(version.RemoteUrl);
    }

    [Fact]
    public async Task AnAvailablePairing_EmitsTheObjectLinkTheTargetDescribes()
    {
        SeedDocument();
        var versionId = SeedVersion(1);
        var sourceId = SeedSource();
        SeedState(sourceId, versionId, "Documents/2025/Lease.pdf", handle: "drive-file-id");

        _target.ObjectUrlPrefix = "https://drive.google.com/file/d/";

        var response = await HandleAsync();

        var version = Assert.Single(Assert.Single(response!.Sources).Versions);
        Assert.Equal("https://drive.google.com/file/d/drive-file-id", version.RemoteUrl);
    }

    // A destination with no browsable URL says so by returning null, rather than the handler inventing a
    // file:// link that leaks the server root and opens nothing.
    [Fact]
    public async Task ATargetWithNoObjectUrl_EmitsNoLink()
    {
        SeedDocument();
        var versionId = SeedVersion(1);
        var sourceId = SeedSource();
        SeedState(sourceId, versionId, "Documents/2025/Lease.pdf");

        var version = Assert.Single(Assert.Single((await HandleAsync())!.Sources).Versions);
        Assert.Null(version.RemoteUrl);
    }

    // The whole point of staging typed failures: "the copy of THIS file failed, and here is why" has to
    // land on the version it belongs to and on no other.
    [Fact]
    public async Task ARecordedFailure_IsMatchedToItsOwnVersion()
    {
        SeedDocument();
        var failedVersion = SeedVersion(2);
        var fineVersion = SeedVersion(1);
        var sourceId = SeedSource();
        SeedState(sourceId, fineVersion, "Documents/2025/_versions/Lease (v1).pdf");

        var completedAt = new DateTimeOffset(2026, 7, 30, 3, 0, 0, TimeSpan.Zero);

        _runHistory.GetLastRunsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BackupRunStatus>
            {
                [sourceId] = new()
                {
                    JobId = Guid.NewGuid(),
                    Status = ProcessingJobStatus.PartiallyCompleted,
                    CreatedAt = completedAt.AddMinutes(-1),
                    CompletedAt = completedAt,
                    TotalChildCount = 1,
                    CompletedChildCount = 0,
                    FailedChildCount = 1,
                    OutOfSpace = false,
                },
            });

        _runHistory.GetActionFailuresAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([
                new BackupActionFailure(
                    failedVersion, "Put", "Documents/2025/Lease.pdf", "Source blob is missing."),
            ]);

        var source = Assert.Single((await HandleAsync())!.Sources);

        var failed = source.Versions.Single(v => v.DocumentVersionId == failedVersion);
        Assert.Equal("Source blob is missing.", failed.LastFailureMessage);
        Assert.Equal(completedAt, failed.LastFailureAt);

        var fine = source.Versions.Single(v => v.DocumentVersionId == fineVersion);
        Assert.Null(fine.LastFailureMessage);
        Assert.Null(fine.LastFailureAt);
    }

    // A run with no failed children has nothing to look up, and asking anyway would walk every batch
    // child the pairing has ever produced.
    [Fact]
    public async Task AHealthyRun_IsNotAskedForActionFailures()
    {
        SeedDocument();
        SeedVersion(1);
        var sourceId = SeedSource();

        _runHistory.GetLastRunsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BackupRunStatus>
            {
                [sourceId] = new()
                {
                    JobId = Guid.NewGuid(),
                    Status = ProcessingJobStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    TotalChildCount = 1,
                    CompletedChildCount = 1,
                    FailedChildCount = 0,
                    OutOfSpace = false,
                },
            });

        await HandleAsync();

        await _runHistory.DidNotReceive()
            .GetActionFailuresAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // A paused destination and a paused pairing are different facts, and the panel has to be able to say
    // which one is stopping the backup.
    [Fact]
    public async Task ADisabledTarget_IsReportedSeparatelyFromADisabledPairing()
    {
        SeedDocument();
        SeedVersion(1);
        SeedSource();

        _db.BackupTargetStates.Add(new BackupTargetState
        {
            TargetKey = TargetKey,
            IsEnabled = false,
            ConnectedAccountEmail = "backups@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var source = Assert.Single((await HandleAsync())!.Sources);

        Assert.False(source.TargetEnabled);
        Assert.True(source.IsEnabled);
        Assert.Equal("backups@example.com", source.TargetAccountEmail);
    }

    // Another document's copies must never be attributed here — the state table is keyed by pairing and
    // document, and a query that dropped the document filter would leak both ways.
    [Fact]
    public async Task AnotherDocumentsStateRows_AreNotCounted()
    {
        SeedDocument();
        var versionId = SeedVersion(1);
        var sourceId = SeedSource();
        SeedState(sourceId, Guid.NewGuid(), "Documents/2025/Other.pdf", documentId: Guid.NewGuid());

        var source = Assert.Single((await HandleAsync())!.Sources);

        Assert.Equal(0, source.CopiedVersionCount);
        Assert.Equal(0, source.PendingRemovalCount);
        Assert.Null(Assert.Single(source.Versions).RemoteRelativePath);
        Assert.Equal(versionId, source.Versions[0].DocumentVersionId);
    }

    /// <summary>
    /// A hand-rolled destination rather than a substitute: <c>DescribeObjectUrl</c> is a default interface
    /// member, and the point of these tests is what a real implementation of it produces.
    /// </summary>
    private sealed class FakeBackupTarget(string key) : IBackupTarget
    {
        public bool Available { get; set; } = true;

        /// <summary>Set to give this target a browsable object URL; null leaves it with none.</summary>
        public string? ObjectUrlPrefix { get; set; }

        public string Key => key;
        public BackupTargetKind? Kind => BackupTargetKind.Local;
        public string? Location => "/tmp/mirror";
        public bool IsAvailable => Available;
        public string? ConfigurationError => Available ? null : "Simulated: this target cannot run.";
        public BackupTargetCapabilities Capabilities => BackupTargetCapabilities.Default;

        public string? DescribeObjectUrl(string? handle) =>
            ObjectUrlPrefix is null || string.IsNullOrWhiteSpace(handle)
                ? null
                : ObjectUrlPrefix + handle;

        public Task<BackupPutResult> PutAsync(
            BackupObjectRef target, Stream content, BackupPutContext context, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(BackupObjectRef target, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
