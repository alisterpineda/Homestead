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

        _tokenManager = new GoogleDriveTokenManager(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IGoogleOAuthClient>(),
            new FakeTimeProvider(Now),
            NullLogger<GoogleDriveTokenManager>.Instance);

        _sut = CreateTarget();
    }

    private readonly GoogleDriveTokenManager _tokenManager;

    /// <summary>
    /// A target over the same fake Drive and the same in-memory store. <paramref name="rootFolderPath"/>
    /// left null is the production default (config unset), which is what most tests want.
    /// </summary>
    private GoogleDriveBackupTarget CreateTarget(string? rootFolderPath = null) => new(
        TargetKey,
        new BackupTargetOptions
        {
            Kind = BackupTargetKind.GoogleDrive,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RootFolderPath = rootFolderPath,
        },
        _tokenManager,
        _services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<GoogleDriveBackupTarget>.Instance,
        new HttpClientFromMessageHandlerFactory(_ =>
            new HttpClientFromMessageHandlerFactory.ConfiguredHttpMessageHandler(_drive, false, false)),
        delay: (wait, _) =>
        {
            _delays.Add(wait);
            return Task.CompletedTask;
        });

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

    /// <summary>Pre-seeds the target-state row, which is where the resolved root path is pinned.</summary>
    private async Task SeedStateAsync(string? rootFolderPath, string? webViewLink = null)
    {
        _db.BackupTargetStates.Add(new BackupTargetState
        {
            TargetKey = TargetKey,
            IsEnabled = true,
            RootFolderPath = rootFolderPath,
            RootFolderWebViewLink = webViewLink,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // AsNoTracking on purpose: the ensure step writes through its own DI scope's context, so a tracking
    // read here would hand back this fixture's stale copy of the row it seeded.
    private Task<BackupTargetState> ReadStateAsync(CancellationToken ct) =>
        _db.BackupTargetStates.AsNoTracking().SingleAsync(s => s.TargetKey == TargetKey, ct);

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

    // --- Location -------------------------------------------------------------------------------------

    // Config supplies the default, so this is what the card reads before anything exists in Drive.
    [Fact]
    public void Location_WithNothingCreatedYet_ReportsTheConfiguredPath()
    {
        Assert.Equal("Google Drive / Homestead/Backup", _sut.Location);
        Assert.Equal("Google Drive / Homestead/Backup", _sut.DescribeLocation(null));

        using var configured = CreateTarget("Archive/Homestead");
        Assert.Equal("Google Drive / Archive/Homestead", configured.Location);
    }

    // The whole point of D5: once the root exists, the DB is the read path — otherwise editing config
    // relabels the card without relocating a single file, and the card claims a location that is false.
    [Fact]
    public void DescribeLocation_PrefersTheResolvedRootOverConfig()
    {
        using var target = CreateTarget("Archive/Homestead");

        Assert.Equal("Google Drive / Legacy/Mirror", target.DescribeLocation("Legacy/Mirror"));
    }

    // Null-when-unconfigured is an interface contract, and a resolved root does not buy an exemption.
    [Fact]
    public void DescribeLocation_OnAnUnconfiguredTarget_IsStillNull()
    {
        var target = new GoogleDriveBackupTarget(
            TargetKey,
            new BackupTargetOptions { Kind = BackupTargetKind.GoogleDrive },
            null!, null!, NullLogger<GoogleDriveBackupTarget>.Instance);

        Assert.Null(target.Location);
        Assert.Null(target.DescribeLocation("Homestead/Backup"));
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

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"backup-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"documents-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"2025-id"}""");

        await _sut.EnsureFoldersAsync(["Documents/2025"], ct);

        var map = await _db.BackupTargetFolders.AsNoTracking()
            .ToDictionaryAsync(f => f.Path, f => f.RemoteFolderId, ct);

        // The EFFECTIVE root is a map entry like any other, at the empty path: the last segment of the
        // root path, not its first.
        Assert.Equal("backup-id", map[string.Empty]);
        Assert.Equal("documents-id", map["Documents"]);
        Assert.Equal("2025-id", map["Documents/2025"]);

        // D3, and load-bearing: the chain ABOVE the effective root must not enter the keyspace at all.
        // That is what keeps the prune walk's slash guard a structural guarantee rather than a rule
        // someone has to remember — the failure mode being trashing the user's own top-level "Homestead".
        Assert.Equal(3, map.Count);
        Assert.DoesNotContain("Homestead", map.Keys);
        Assert.DoesNotContain("Homestead/Backup", map.Keys);
    }

    [Fact]
    public async Task EnsureFolders_AdoptsAnExistingFolderRatherThanCreatingATwin()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.RespondToQuery("name = 'Homestead'", """{"files":[{"id":"existing-homestead"}]}""");
        _drive.RespondToQuery("name = 'Backup'", """{"files":[{"id":"existing-backup"}]}""");

        await _sut.EnsureFoldersAsync([string.Empty], ct);

        var row = await _db.BackupTargetFolders.AsNoTracking().SingleAsync(f => f.Path == string.Empty, ct);
        Assert.Equal("existing-backup", row.RemoteFolderId);
        Assert.Empty(_drive.Where(HttpMethod.Post));
    }

    // Steady state has to cost zero folder calls, or every run pays for the whole tree — and a mapped
    // root has to short-circuit the whole chain above it, not just its own segment. The pin is part of
    // steady state, not decoration: whatever created the root recorded it in the same flush.
    [Fact]
    public async Task EnsureFolders_WithAFullyMappedTree_TouchesGoogleNotAtAll()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStateAsync("Homestead/Backup");
        await MapFolderAsync(string.Empty, "backup-id");
        await MapFolderAsync("Documents", "documents-id");
        await MapFolderAsync("Documents/2025", "2025-id");

        await _sut.EnsureFoldersAsync(["Documents/2025"], ct);

        Assert.Empty(_drive.Calls);
    }

    // The map row and the state row are ONE transaction, so a mapped root always has its path recorded.
    // That invariant is what lets the root branch trust the map alone — see EnsureRootLockedAsync.
    [Fact]
    public async Task EnsureFolders_WritesTheMapRowAndThePinTogether()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"backup-id"}""");

        await _sut.EnsureFoldersAsync([], ct);

        Assert.Equal("backup-id", (await _db.BackupTargetFolders.AsNoTracking()
            .SingleAsync(f => f.Path == string.Empty, ct)).RemoteFolderId);
        Assert.Equal("Homestead/Backup", (await ReadStateAsync(ct)).RootFolderPath);
    }

    // Every run reads the root from here, and the card renders from it — so a run that creates the root
    // and fails to record it would leave the card guessing from config forever.
    [Fact]
    public async Task EnsureFolders_RecordsTheRootPathAndWebViewLinkOnTheTargetState()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files",
            """{"id":"backup-id","webViewLink":"https://drive.google.com/drive/folders/backup-id"}""");

        // No state row beforehand — this is the upsert path, and it is the common one: a target whose
        // grant was stored before this column existed has a row, one that never connected has none.
        await _sut.EnsureFoldersAsync([], ct);

        var state = await ReadStateAsync(ct);
        Assert.Equal("Homestead/Backup", state.RootFolderPath);
        Assert.Equal("https://drive.google.com/drive/folders/backup-id", state.RootFolderWebViewLink);
    }

    [Fact]
    public async Task EnsureFolders_WithAnExistingStateRow_UpdatesItInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStateAsync(rootFolderPath: null);

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"backup-id"}""");

        await _sut.EnsureFoldersAsync([], ct);

        var state = await ReadStateAsync(ct);
        Assert.Equal("Homestead/Backup", state.RootFolderPath);
        Assert.True(state.IsEnabled);
    }

    // The pin. DocumentBackupState rows hold root-relative paths plus Drive file ids, so honouring a
    // changed config after creation would mint a new empty root while every already-copied file stayed
    // physically in the old tree — a mirror reporting converged and silently wrong.
    [Fact]
    public async Task EnsureFolders_WithAPinnedRootPath_IgnoresTheConfiguredPath()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStateAsync("Legacy/Mirror");

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"legacy-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"mirror-id"}""");

        await _sut.EnsureFoldersAsync([], ct);

        Assert.Contains(_drive.Calls, c => c.Query.Contains("name = 'Legacy'"));
        Assert.Contains(_drive.Calls, c => c.Query.Contains("name = 'Mirror'"));
        Assert.DoesNotContain(_drive.Calls, c => c.Query.Contains("name = 'Homestead'"));

        var row = await _db.BackupTargetFolders.AsNoTracking().SingleAsync(f => f.Path == string.Empty, ct);
        Assert.Equal("mirror-id", row.RemoteFolderId);

        // Rewritten, not replaced — the pin survives its own re-resolution.
        Assert.Equal("Legacy/Mirror", (await ReadStateAsync(ct)).RootFolderPath);
    }

    [Fact]
    public async Task EnsureFolders_WithAConfiguredPath_UsesItInsteadOfTheDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        using var target = CreateTarget("Archive/Homestead");

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"archive-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");

        await target.EnsureFoldersAsync([], ct);

        Assert.Contains(_drive.Calls, c => c.Query.Contains("name = 'Archive'"));

        var row = await _db.BackupTargetFolders.AsNoTracking().SingleAsync(f => f.Path == string.Empty, ct);
        Assert.Equal("homestead-id", row.RemoteFolderId);
        Assert.Equal("Archive/Homestead", (await ReadStateAsync(ct)).RootFolderPath);
    }

    // A miss at write time (a crash gap, or a folder removed out of band) resolves on demand rather than
    // failing the action.
    [Fact]
    public async Task Put_WithAnUnmappedFolder_EnsuresItOnDemand()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"backup-id"}""");
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

    // Nothing above the effective root may ever be trashed, and the guarantee is STRUCTURAL rather than a
    // rule: the chain gets no map keys, so the walk-up cannot name it and the slash guard stops at the
    // kind root regardless. The failure mode this defends is trashing the user's own top-level "Homestead".
    [Fact]
    public async Task Prune_NeverReachesTheRootChain()
    {
        var ct = TestContext.Current.CancellationToken;

        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"homestead-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"backup-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"documents-id"}""");
        _drive.Respond(HttpMethod.Post, "/drive/v3/files", """{"id":"2025-id"}""");

        await _sut.EnsureFoldersAsync(["Documents/2025"], ct);

        // Everything reads empty (the fake's default), so the walk-up goes as far as it structurally can.
        await _sut.PruneEmptyFoldersAsync(["Documents/2025"], ct);

        var trashed = _drive.Where(HttpMethod.Patch).Select(c => c.Url).ToList();

        Assert.Contains(trashed, u => u.Contains("2025-id"));
        Assert.DoesNotContain(trashed, u => u.Contains("documents-id"));
        Assert.DoesNotContain(trashed, u => u.Contains("backup-id"));
        Assert.DoesNotContain(trashed, u => u.Contains("homestead-id"));
    }

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
