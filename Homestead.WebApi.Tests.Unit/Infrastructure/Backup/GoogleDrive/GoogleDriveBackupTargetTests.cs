using System.Net;
using System.Text;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Persistence;
using Google.Apis.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

public class GoogleDriveBackupTargetTests : IDisposable
{
    private const string TargetKey = "gdrive";
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly TestAppDbContext _db;
    private readonly FakeDriveHandler _drive = new();
    private readonly ServiceProvider _services;
    private readonly GoogleDriveBackupTarget _sut;

    /// <summary>Every backoff the target asked for, so rate-limit handling is assertable without waiting.</summary>
    private readonly List<TimeSpan> _delays = [];

    public GoogleDriveBackupTargetTests()
    {
        _db = TestAppDbContext.Create(_databaseName);

        var store = Substitute.For<IGoogleDriveGrantStore>();
        store.ReadAsync(TargetKey, Arg.Any<CancellationToken>())
            .Returns(new GrantReadResult(
                GrantReadStatus.Ok, new GoogleTokenGrant("rt", "access-token", Now.AddHours(1))));

        // Each DI scope gets its OWN context over the same in-memory store — the scope disposes what it
        // creates, so handing it `_db` would leave this fixture holding a disposed context.
        _services = new ServiceCollection()
            .AddScoped<AppDbContext>(_ => TestAppDbContext.Create(_databaseName))
            .AddScoped(_ => store)
            .BuildServiceProvider();

        var tokenManager = new GoogleDriveTokenManager(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IGoogleOAuthClient>(),
            new FakeTimeProvider(Now),
            NullLogger<GoogleDriveTokenManager>.Instance);

        _sut = new GoogleDriveBackupTarget(
            TargetKey,
            new BackupTargetOptions
            {
                Kind = BackupTargetKind.GoogleDrive,
                ClientId = "client-id",
                ClientSecret = "client-secret",
            },
            tokenManager,
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GoogleDriveBackupTarget>.Instance,
            new HttpClientFromMessageHandlerFactory(_ =>
                new HttpClientFromMessageHandlerFactory.ConfiguredHttpMessageHandler(_drive, false, false)),
            delay: (wait, _) =>
            {
                _delays.Add(wait);
                return Task.CompletedTask;
            });
    }

    public void Dispose()
    {
        _sut.Dispose();
        _db.Dispose();
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Stream Content(string text = "payload") => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static BackupPutContext Ctx(string? sha256 = null, string? md5 = null) =>
        new("application/pdf", Sha256Hex: sha256, Md5Hex: md5);

    /// <summary>Pre-seeds the folder map so a test can go straight to the operation under test.</summary>
    private async Task MapFolderAsync(string path, string folderId)
    {
        _db.BackupTargetFolders.Add(new BackupTargetFolder
        {
            Id = Guid.NewGuid(),
            TargetKey = TargetKey,
            Path = path,
            RemoteFolderId = folderId,
            CreatedAt = Now,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // --- Shape ----------------------------------------------------------------------------------------

    [Fact]
    public void DeclaresContentHashSupportAndADeferredFolderPrune()
    {
        Assert.Equal(BackupTargetKind.GoogleDrive, _sut.Kind);
        Assert.True(_sut.IsAvailable);
        Assert.True(_sut.Capabilities.SupportsContentHash);
        Assert.True(_sut.Capabilities.SupportsMove);

        // Listing is eventually consistent here, so an inline emptiness check would race the run's own
        // moves — the planner reads this to emit a final sweep instead.
        Assert.True(_sut.Capabilities.DefersFolderPrune);
    }

    // The engine's caps were sized against local disk; against a residential uplink they are wildly wrong.
    [Fact]
    public void OverridesTheBatchCapsForAnUplinkRatherThanADisk()
    {
        Assert.Equal(25, _sut.BatchCapOverrides.MaxFilesPerBatch);
        Assert.Equal(64L * 1024 * 1024, _sut.BatchCapOverrides.MaxBytesPerBatch);
        Assert.Equal(50, _sut.BatchCapOverrides.MaxOpsPerMetadataBatch);
    }

    [Fact]
    public void AnExplicitOverrideBeatsTheKindDefault()
    {
        var target = new GoogleDriveBackupTarget(
            TargetKey,
            new BackupTargetOptions
            {
                Kind = BackupTargetKind.GoogleDrive,
                ClientId = "id",
                ClientSecret = "secret",
                MaxFilesPerBatch = 5,
            },
            null!, null!, NullLogger<GoogleDriveBackupTarget>.Instance);

        Assert.Equal(5, target.BatchCapOverrides.MaxFilesPerBatch);
        Assert.Equal(64L * 1024 * 1024, target.BatchCapOverrides.MaxBytesPerBatch);
    }

    [Fact]
    public void MissingCredentials_AreConfigurationErrorsRatherThanCrashes()
    {
        var target = new GoogleDriveBackupTarget(
            TargetKey,
            new BackupTargetOptions { Kind = BackupTargetKind.GoogleDrive },
            null!, null!, NullLogger<GoogleDriveBackupTarget>.Instance);

        Assert.False(target.IsAvailable);
        Assert.Contains("ClientId is required", target.ConfigurationError);
    }

    // --- Put ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_WithAStoredHandle_PatchesThatIdAndNeverSearchesByName()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1", """{"id":"file-1"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", "file-1"), Content(), Ctx(), ct);

        Assert.Equal(new BackupPutResult.Completed("file-1"), result);
        Assert.DoesNotContain(_drive.Calls, c => c.Query.Contains("name ="));
    }

    // Normally a no-op; a resurrection when the user has tidied the mirror into their trash. Without it
    // the update SUCCEEDS against a trashed file and the card shows a converged mirror that is 30 days
    // from being purged.
    [Fact]
    public async Task Put_ByHandle_AssertsTrashedFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1", """{"id":"file-1"}""");

        await _sut.PutAsync(new BackupObjectRef("Documents/2025/Doc.pdf", "file-1"), Content(), Ctx(), ct);

        var patch = Assert.Single(_drive.Where(HttpMethod.Patch).Where(c => c.Body.Contains("trashed")));
        Assert.Contains("\"trashed\":false", patch.Body);
    }

    // The crash window: a previous run wrote the file and died before its state row landed. Without the
    // lookup every crash leaves a permanent duplicate nothing ever notices.
    [Fact]
    public async Task Put_WithoutAHandle_AdoptsAnExistingFileOfTheSameName()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2025", "folder-2025");

        _drive.RespondToQuery("name = 'Doc.pdf'", """{"files":[{"id":"orphan-1"}]}""");
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/orphan-1", """{"id":"orphan-1"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", null), Content(), Ctx(), ct);

        Assert.Equal(new BackupPutResult.Completed("orphan-1"), result);
    }

    // Drive legally holds duplicate names in one folder, so a crash LOOP can leave several. Keep the
    // newest and trash the rest — they are invisible otherwise, since nothing enumerates the destination.
    [Fact]
    public async Task Put_WithoutAHandle_TrashesSurplusDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2025", "folder-2025");

        _drive.RespondToQuery(
            "name = 'Doc.pdf'", """{"files":[{"id":"newest"},{"id":"older"},{"id":"oldest"}]}""");
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/newest", """{"id":"newest"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", null), Content(), Ctx(), ct);

        Assert.Equal(new BackupPutResult.Completed("newest"), result);

        var trashed = _drive.Where(HttpMethod.Patch)
            .Where(c => c.Body.Contains("\"trashed\":true"))
            .Select(c => c.Url)
            .ToList();

        Assert.Contains(trashed, u => u.Contains("/files/older"));
        Assert.Contains(trashed, u => u.Contains("/files/oldest"));
        Assert.DoesNotContain(trashed, u => u.Contains("/files/newest"));
    }

    // A name carrying a quote or a backslash would otherwise break out of the query literal.
    [Theory]
    [InlineData("O'Brien's bill.pdf", @"name = 'O\'Brien\'s bill.pdf'")]
    [InlineData(@"back\slash.pdf", @"name = 'back\\slash.pdf'")]
    public async Task Put_EscapesTheNameInTheCrashWindowQuery(string name, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2025", "folder-2025");

        await _sut.PutAsync(new BackupObjectRef($"Documents/2025/{name}", null), Content(), Ctx(), ct);

        Assert.Contains(_drive.Calls, c => c.Query.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Put_WithNoMatch_CreatesTheFileUnderTheMappedFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2025", "folder-2025");

        _drive.Respond(HttpMethod.Post, "/upload/drive/v3/files", """{"id":"created"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", null), Content(), Ctx(), ct);

        Assert.Equal(new BackupPutResult.Completed("created"), result);
        Assert.Contains(_drive.Where(HttpMethod.Post), c => c.Body.Contains("folder-2025"));
    }

    // --- Checksums ------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_MatchingSha256_IsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1",
            """{"id":"file-1","sha256Checksum":"ABCDEF"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/Doc.pdf", "file-1"), Content(), Ctx(sha256: "abcdef"), ct);

        Assert.Equal(new BackupPutResult.Completed("file-1"), result);
    }

    // sha256Checksum is documented "if available"; MD5 rides along in the same pre-write pass so an
    // absent one still leaves something to verify against without downloading the file back.
    [Fact]
    public async Task Put_FallsBackToMd5WhenDriveReportsNoSha256()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1",
            """{"id":"file-1","md5Checksum":"deadbeef"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/Doc.pdf", "file-1"), Content(),
            Ctx(sha256: "abcdef", md5: "deadbeef"), ct);

        Assert.Equal(new BackupPutResult.Completed("file-1"), result);
    }

    // Both absent is trust-and-record: the pre-write hash check has already guarded the content.
    [Fact]
    public async Task Put_WithNoChecksumAtAll_IsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1", """{"id":"file-1"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/Doc.pdf", "file-1"), Content(), Ctx(sha256: "abcdef"), ct);

        Assert.Equal(new BackupPutResult.Completed("file-1"), result);
    }

    // Leaving corrupt bytes behind would let the NEXT run's crash-window lookup find them under the right
    // name and adopt them as the mirror.
    [Fact]
    public async Task Put_ChecksumMismatch_TrashesTheUploadAndFailsTheAction()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1",
            """{"id":"file-1","sha256Checksum":"999999"}""");
        _drive.Respond(HttpMethod.Patch, "/drive/v3/files/file-1", """{"id":"file-1"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PutAsync(
            new BackupObjectRef("Documents/Doc.pdf", "file-1"), Content(), Ctx(sha256: "abcdef"), ct));

        Assert.Contains("different checksum", ex.Message);
        Assert.Contains(_drive.Where(HttpMethod.Patch), c => c.Body.Contains("\"trashed\":true"));
    }

    // --- Move -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Move_IsAMetadataPatchWithBothParentEdits()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2025", "folder-2025");
        await MapFolderAsync("Documents/2026", "folder-2026");

        _drive.Respond(HttpMethod.Patch, "/drive/v3/files/file-1", """{"id":"file-1"}""");

        var handle = await _sut.MoveAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", "file-1"), "Documents/2026/Renamed.pdf", ct);

        Assert.Equal("file-1", handle);

        var patch = Assert.Single(_drive.Where(HttpMethod.Patch));
        Assert.Contains("addParents=folder-2026", patch.Url);
        // Drive enforces a single parent, so the removeParents half is not optional bookkeeping.
        Assert.Contains("removeParents=folder-2025", patch.Url);
        Assert.Contains("\"name\":\"Renamed.pdf\"", patch.Body);
        Assert.Contains("\"trashed\":false", patch.Body);
    }

    // The batch handler's self-heal contract: a FileNotFoundException drops the state row so the next
    // run re-copies from scratch.
    [Fact]
    public async Task Move_404_ThrowsFileNotFoundAndForgetsTheFolderMapping()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2026", "folder-2026");

        _drive.Fail(HttpMethod.Patch, "/drive/v3/files/file-1", HttpStatusCode.NotFound, "notFound");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.MoveAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", "file-1"), "Documents/2026/Doc.pdf", ct));

        Assert.False(await _db.BackupTargetFolders.AnyAsync(f => f.Path == "Documents/2026", ct));
    }

    [Fact]
    public async Task Move_WithNoStoredId_ReportsAMissingFileRatherThanGuessing()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.MoveAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", null), "Documents/2026/Doc.pdf",
            TestContext.Current.CancellationToken));
    }

    // --- Delete ---------------------------------------------------------------------------------------

    // files.delete permanently deletes bypassing the trash, and on a folder it takes every descendant the
    // user owns — including files this app's scope cannot see. It is never called.
    [Fact]
    public async Task Delete_TrashesAndNeverIssuesAnHttpDelete()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Respond(HttpMethod.Patch, "/drive/v3/files/file-1", """{"id":"file-1"}""");

        await _sut.DeleteAsync(new BackupObjectRef("Documents/Doc.pdf", "file-1"), ct);

        var patch = Assert.Single(_drive.Where(HttpMethod.Patch));
        Assert.Contains("\"trashed\":true", patch.Body);
        Assert.Empty(_drive.Where(HttpMethod.Delete));
    }

    [Fact]
    public async Task Delete_404_IsIdempotentByContract()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Fail(HttpMethod.Patch, "/drive/v3/files/file-1", HttpStatusCode.NotFound, "notFound");

        await _sut.DeleteAsync(new BackupObjectRef("Documents/Doc.pdf", "file-1"), ct);
    }

    // --- Folder ensure --------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureFolders_CreatesEveryAncestorAndPersistsTheMap()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"root-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"documents-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"2025-id"}""");

        await _sut.EnsureFoldersAsync(["Documents/2025"], ct);

        var map = await _db.BackupTargetFolders.AsNoTracking()
            .ToDictionaryAsync(f => f.Path, f => f.RemoteFolderId, ct);

        // The target root is a map entry like any other, at the empty path.
        Assert.Equal("root-id", map[string.Empty]);
        Assert.Equal("documents-id", map["Documents"]);
        Assert.Equal("2025-id", map["Documents/2025"]);
    }

    [Fact]
    public async Task EnsureFolders_AdoptsAnExistingFolderRatherThanCreatingATwin()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.RespondToQuery("name = 'Homestead Backup'", """{"files":[{"id":"existing-root"}]}""");

        await _sut.EnsureFoldersAsync([string.Empty], ct);

        var row = await _db.BackupTargetFolders.AsNoTracking().SingleAsync(f => f.Path == string.Empty, ct);
        Assert.Equal("existing-root", row.RemoteFolderId);
        Assert.Empty(_drive.Where(HttpMethod.Post));
    }

    // Steady state has to cost zero folder calls, or every run pays for the whole tree.
    [Fact]
    public async Task EnsureFolders_WithAFullyMappedTree_TouchesGoogleNotAtAll()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync(string.Empty, "root-id");
        await MapFolderAsync("Documents", "documents-id");
        await MapFolderAsync("Documents/2025", "2025-id");

        await _sut.EnsureFoldersAsync(["Documents/2025"], ct);

        Assert.Empty(_drive.Calls);
    }

    // A miss at write time (a crash gap, or a folder removed out of band) resolves on demand rather than
    // failing the action.
    [Fact]
    public async Task Put_WithAnUnmappedFolder_EnsuresItOnDemand()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"root-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"documents-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"2099-id"}""");
        _drive.Respond(HttpMethod.Post, "/upload/drive/v3/files", """{"id":"created"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/2099/New.pdf", null), Content(), Ctx(), ct);

        Assert.Equal(new BackupPutResult.Completed("created"), result);
        Assert.True(await _db.BackupTargetFolders.AnyAsync(f => f.Path == "Documents/2099", ct));
    }

    // A 404 on a mapped id fails that action and invalidates the row; the next run's ensure repairs it.
    [Fact]
    public async Task Put_404OnAMappedFolder_DropsTheMappingAndFailsTheAction()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents/2025", "stale-folder");

        _drive.Fail(HttpMethod.Post, "/upload/drive/v3/files", HttpStatusCode.NotFound, "notFound");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PutAsync(
            new BackupObjectRef("Documents/2025/Doc.pdf", null), Content(), Ctx(), ct));

        Assert.Contains("no longer exists", ex.Message);
        Assert.False(await _db.BackupTargetFolders.AnyAsync(f => f.Path == "Documents/2025", ct));
    }

    // --- Folder prune ---------------------------------------------------------------------------------

    [Fact]
    public async Task Prune_TrashesAnEmptyFolderAndWalksUpToTheKindRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync(string.Empty, "root-id");
        await MapFolderAsync("Documents", "documents-id");
        await MapFolderAsync("Documents/2025", "2025-id");
        await MapFolderAsync("Documents/2025/_versions", "versions-id");

        // Everything reads empty (the handler's default).
        await _sut.PruneEmptyFoldersAsync(["Documents/2025/_versions"], ct);

        var trashed = _drive.Where(HttpMethod.Patch).Select(c => c.Url).ToList();
        Assert.Contains(trashed, u => u.Contains("versions-id"));
        Assert.Contains(trashed, u => u.Contains("2025-id"));

        // Stops at the kind root: "Documents" is never trashed, nor is the target root.
        Assert.DoesNotContain(trashed, u => u.Contains("documents-id"));
        Assert.DoesNotContain(trashed, u => u.Contains("root-id"));

        Assert.False(await _db.BackupTargetFolders.AnyAsync(f => f.Path == "Documents/2025", ct));
        Assert.True(await _db.BackupTargetFolders.AnyAsync(f => f.Path == "Documents", ct));
    }

    [Fact]
    public async Task Prune_LeavesANonEmptyFolderAloneAndStopsWalking()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents", "documents-id");
        await MapFolderAsync("Documents/2025", "2025-id");

        _drive.RespondToQuery("'2025-id' in parents", """{"files":[{"id":"still-here"}]}""");

        await _sut.PruneEmptyFoldersAsync(["Documents/2025"], ct);

        Assert.Empty(_drive.Where(HttpMethod.Patch));
        Assert.True(await _db.BackupTargetFolders.AnyAsync(f => f.Path == "Documents/2025", ct));
    }

    // Best-effort by design: an empty folder left behind is cosmetic, and failing the run's last batch
    // over housekeeping would paint a fully-converged mirror red.
    [Fact]
    public async Task Prune_SwallowsAPerFolderFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        await MapFolderAsync("Documents", "documents-id");
        await MapFolderAsync("Documents/2025", "2025-id");

        _drive.Fail(HttpMethod.Get, "/drive/v3/files", HttpStatusCode.InternalServerError, "backendError");

        await _sut.PruneEmptyFoldersAsync(["Documents/2025"], ct);
    }

    [Fact]
    public async Task Prune_IgnoresAnUnmappedFolder()
    {
        await _sut.PruneEmptyFoldersAsync(
            ["Documents/2025"], TestContext.Current.CancellationToken);

        Assert.Empty(_drive.Calls);
    }

    // --- Error classification -------------------------------------------------------------------------

    // Drive reports storage exhaustion, per-user throttling, and project throttling ALL as HTTP 403,
    // distinguished only by reason — and `quotaExceeded`, confusingly, means *rate*.
    [Fact]
    public async Task StorageQuotaExceeded_BecomesTheTypedOutOfSpaceException()
    {
        var ct = TestContext.Current.CancellationToken;
        _drive.Fail(HttpMethod.Patch, "/drive/v3/files/file-1",
            HttpStatusCode.Forbidden, "storageQuotaExceeded");

        await Assert.ThrowsAsync<BackupTargetOutOfSpaceException>(() =>
            _sut.DeleteAsync(new BackupObjectRef("Documents/Doc.pdf", "file-1"), ct));

        // Never retried: every attempt would burn the full backoff ladder against a wall.
        Assert.Empty(_delays);
    }

    [Theory]
    [InlineData("userRateLimitExceeded")]
    [InlineData("rateLimitExceeded")]
    [InlineData("quotaExceeded")]
    public async Task RateLimits_AreRetriedWithExponentialBackoff(string reason)
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
            _drive.Fail(HttpMethod.Patch, "/drive/v3/files/file-1", HttpStatusCode.Forbidden, reason);

        _drive.Respond(HttpMethod.Patch, "/drive/v3/files/file-1", """{"id":"file-1"}""");

        await _sut.DeleteAsync(new BackupObjectRef("Documents/Doc.pdf", "file-1"), ct);

        Assert.Equal(
            [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], _delays);
    }

    // Throttling during an UPLOAD has to back off too, not just during metadata ops — puts are the bulk
    // of a run's work, and failing one costs a whole file's re-copy on the next run.
    [Fact]
    public async Task AThrottledUpload_IsRetriedWithBackoff()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Fail(HttpMethod.Patch, "/upload/drive/v3/files/file-1",
            HttpStatusCode.Forbidden, "userRateLimitExceeded");
        _drive.Respond(HttpMethod.Patch, "/upload/drive/v3/files/file-1", """{"id":"file-1"}""");

        var result = await _sut.PutAsync(
            new BackupObjectRef("Documents/Doc.pdf", "file-1"), Content(), Ctx(), ct);

        Assert.Equal(new BackupPutResult.Completed("file-1"), result);
        Assert.Equal([TimeSpan.FromMilliseconds(500)], _delays);
    }

    // Exhaustion cannot be waited out — every retry would burn the ladder against a wall.
    [Fact]
    public async Task AnOutOfSpaceUpload_IsNotRetried()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
            _drive.Fail(HttpMethod.Patch, "/upload/drive/v3/files/file-1",
                HttpStatusCode.Forbidden, "storageQuotaExceeded");

        await Assert.ThrowsAsync<BackupTargetOutOfSpaceException>(() => _sut.PutAsync(
            new BackupObjectRef("Documents/Doc.pdf", "file-1"), Content(), Ctx(), ct));

        Assert.Empty(_delays);
    }

    [Fact]
    public async Task PersistentRateLimiting_EventuallyGivesUp()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 10; i++)
            _drive.Fail(HttpMethod.Patch, "/drive/v3/files/file-1",
                HttpStatusCode.Forbidden, "userRateLimitExceeded");

        await Assert.ThrowsAsync<Google.GoogleApiException>(() =>
            _sut.DeleteAsync(new BackupObjectRef("Documents/Doc.pdf", "file-1"), ct));

        Assert.Equal(4, _delays.Count);
    }
}
