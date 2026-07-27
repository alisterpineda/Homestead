using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

/// <summary>
/// End-to-end coverage of the one-way document backup mirror against a real filesystem target: the
/// planner's layout, the incremental diff (put / move / delete / no-op), the bulk-delete circuit
/// breaker, and the run/schedule endpoints. The pairing itself (creation, deletion, enablement) is
/// covered by <see cref="Homestead.WebApi.Tests.Integration.Admin.BackupSourceApiTests"/>.
/// </summary>
[Collection("Integration")]
public class BackupMirrorTests : IntegrationTestBase
{
    private const string TargetKey = HomesteadApiFactory.BackupTargetKey;

    /// <summary>Every mirrored path sits under the source kind's own folder at the target root.</summary>
    private const string Root = DocumentsBackupSourceDescriptor.RootFolderName;

    public BackupMirrorTests(PostgresFixture fixture) : base(fixture) { }

    // --- helpers ---

    private string MirrorRoot => Factory.BackupPath;

    /// <summary>Every file in the mirror, as forward-slash paths relative to the target root.</summary>
    private List<string> MirrorFiles() =>
        Directory.Exists(MirrorRoot)
            ? [.. Directory.EnumerateFiles(MirrorRoot, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(MirrorRoot, p).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(p => p, StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// Creates the "mirror Documents to local" pairing every test in this class runs against. Respawn
    /// wipes it between tests, so it is bootstrapped per test rather than once.
    /// </summary>
    private async Task<Guid> CreateSourceAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/backup/sources",
            new { sourceKind = DocumentsBackupSourceDescriptor.SourceKey, targetKey = TargetKey }, CT);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SourceResult>(CT);
        return body!.Id;
    }

    /// <summary>Runs a full backup and waits for the parent and every batch child to terminate.</summary>
    private async Task<ProcessingJob> RunBackupAsync(
        HttpClient client, Guid sourceId, bool dryRun = false, bool allowLargePrune = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs",
            new { dryRun, allowLargePrune }, CT);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RunResult>(CT);
        return await WaitForBackupJobAsync(body!.JobId);
    }

    private async Task<ProcessingJob> WaitForBackupJobAsync(Guid jobId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(25));

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        while (DateTime.UtcNow < deadline)
        {
            var job = await db.ProcessingJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, CT);

            if (job is not null && job.Status is ProcessingJobStatus.Completed
                or ProcessingJobStatus.Failed or ProcessingJobStatus.PartiallyCompleted
                or ProcessingJobStatus.Cancelled)
            {
                return job;
            }

            await Task.Delay(150, CT);
        }

        throw new TimeoutException($"Backup job {jobId} did not reach a terminal status within timeout");
    }

    private async Task<ProcessingJob> RunBackupExpectingSuccessAsync(
        HttpClient client, Guid sourceId, bool dryRun = false, bool allowLargePrune = false)
    {
        var job = await RunBackupAsync(client, sourceId, dryRun, allowLargePrune);
        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        return job;
    }

    private async Task SetDocumentDateAsync(Guid documentId, DateOnly? documentDate)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await db.Documents
            .IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .FirstAsync(d => d.Id == documentId, CT);
        document.DocumentDate = documentDate;
        await db.SaveChangesAsync(CT);
    }

    private async Task RenameDocumentAsync(Guid documentId, string name)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await db.Documents
            .IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .FirstAsync(d => d.Id == documentId, CT);
        document.Name = name;
        await db.SaveChangesAsync(CT);
    }

    private async Task<int> StateRowCountAsync(Guid sourceId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DocumentBackupStates.CountAsync(s => s.BackupSourceId == sourceId, CT);
    }

    // The suite configures more than one target now (a Local one and a GoogleDrive one), so "the single
    // card" is no longer a valid way to reach the one these tests mirror to.
    private static TargetItem LocalTarget(TargetsResult targets) =>
        targets.Items.Single(t => t.Key == TargetKey);

    private async Task<SourceItem> GetSourceAsync(HttpClient client, Guid sourceId)
    {
        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        return LocalTarget(targets!).Sources.Single(s => s.Id == sourceId);
    }

    private async Task AddVersionAndWaitAsync(HttpClient client, Guid documentId)
    {
        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "v2.pdf");

        var response = await client.PostAsync($"/api/v1/documents/{documentId}/versions", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(body!.JobId);
    }

    // --- the layout contract ---

    [Fact]
    public async Task FirstRun_ReproducesTheExpectedTree()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var dated = await UploadAndWaitAsync(client, "dated.pdf");
        await RenameDocumentAsync(dated, "Tax Return 2024");
        await SetDocumentDateAsync(dated, new DateOnly(2024, 4, 15));

        var undated = await UploadAndWaitAsync(client, "undated.pdf");
        await RenameDocumentAsync(undated, "Misc Receipt");

        await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal(
            [$"{Root}/2024/Tax Return 2024.pdf", $"{Root}/_undated/Misc Receipt.pdf"],
            MirrorFiles());
    }

    [Fact]
    public async Task MultipleVersions_LatestAtShardRoot_SupersededUnderVersionsFolder()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "lease.pdf");
        await RenameDocumentAsync(docId, "Lease");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));
        await AddVersionAndWaitAsync(client, docId);

        await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal(
            [$"{Root}/2025/Lease.pdf", $"{Root}/2025/_versions/Lease (v1).pdf"],
            MirrorFiles());
    }

    [Fact]
    public async Task MirroredBytesMatchTheOriginal()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "payload.pdf");
        await RenameDocumentAsync(docId, "Payload");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.DocumentId == docId, CT);

        var original = await File.ReadAllBytesAsync(
            Path.Combine(Factory.MediaPath, version.FilePath), CT);
        var mirrored = await File.ReadAllBytesAsync(
            Path.Combine(MirrorRoot, Root, "2025", "Payload.pdf"), CT);

        Assert.Equal(original, mirrored);
    }

    // --- the incremental diff ---

    [Fact]
    public async Task SecondRunWithNoChanges_DoesNothing()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "stable.pdf");
        await RenameDocumentAsync(docId, "Stable");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);

        var mirrored = Path.Combine(MirrorRoot, Root, "2025", "Stable.pdf");
        var writtenAt = File.GetLastWriteTimeUtc(mirrored);

        var second = await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal(0, second.TotalChildCount);
        Assert.Equal([$"{Root}/2025/Stable.pdf"], MirrorFiles());
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(mirrored));
    }

    [Fact]
    public async Task RenamingADocument_MovesTheFileRatherThanDuplicatingIt()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "before.pdf");
        await RenameDocumentAsync(docId, "Before");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);
        Assert.Equal([$"{Root}/2025/Before.pdf"], MirrorFiles());

        await RenameDocumentAsync(docId, "After");
        await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal([$"{Root}/2025/After.pdf"], MirrorFiles());
        Assert.Equal(1, await StateRowCountAsync(sourceId));
    }

    [Fact]
    public async Task ChangingTheDocumentDate_ReShardsEveryVersion()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "shifting.pdf");
        await RenameDocumentAsync(docId, "Shifting");
        await SetDocumentDateAsync(docId, new DateOnly(2024, 6, 1));
        await AddVersionAndWaitAsync(client, docId);

        await RunBackupExpectingSuccessAsync(client, sourceId);
        Assert.Equal(
            [$"{Root}/2024/Shifting.pdf", $"{Root}/2024/_versions/Shifting (v1).pdf"],
            MirrorFiles());

        await SetDocumentDateAsync(docId, new DateOnly(2026, 6, 1));
        await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal(
            [$"{Root}/2026/Shifting.pdf", $"{Root}/2026/_versions/Shifting (v1).pdf"],
            MirrorFiles());
    }

    [Fact]
    public async Task AddingAVersion_DemotesTheOldLatestIntoTheVersionsFolder()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "growing.pdf");
        await RenameDocumentAsync(docId, "Growing");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);
        Assert.Equal([$"{Root}/2025/Growing.pdf"], MirrorFiles());

        await AddVersionAndWaitAsync(client, docId);
        await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal(
            [$"{Root}/2025/Growing.pdf", $"{Root}/2025/_versions/Growing (v1).pdf"],
            MirrorFiles());
        Assert.Equal(2, await StateRowCountAsync(sourceId));
    }

    // Trash is a restorable state — a backup that evaporated the moment a file hit the trash would be
    // exactly the copy you needed.
    [Fact]
    public async Task TrashingADocument_LeavesTheMirrorUntouched()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "doomed.pdf");
        await RenameDocumentAsync(docId, "Doomed");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);
        Assert.Equal([$"{Root}/2025/Doomed.pdf"], MirrorFiles());

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/{docId}", CT);
        deleteResponse.EnsureSuccessStatusCode();

        var second = await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal(0, second.TotalChildCount);
        Assert.Equal([$"{Root}/2025/Doomed.pdf"], MirrorFiles());
    }

    [Fact]
    public async Task PermanentlyDeletingADocument_RemovesItFromTheMirror()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var keeper = await UploadAndWaitAsync(client, "keeper.pdf");
        await RenameDocumentAsync(keeper, "Keeper");
        await SetDocumentDateAsync(keeper, new DateOnly(2025, 1, 1));

        var doomed = await UploadAndWaitAsync(client, "doomed.pdf");
        await RenameDocumentAsync(doomed, "Doomed");
        await SetDocumentDateAsync(doomed, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);
        Assert.Equal([$"{Root}/2025/Doomed.pdf", $"{Root}/2025/Keeper.pdf"], MirrorFiles());

        (await client.DeleteAsync($"/api/v1/documents/{doomed}", CT)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/documents/{doomed}/permanently-delete", null, CT))
            .EnsureSuccessStatusCode();

        await RunBackupExpectingSuccessAsync(client, sourceId);

        Assert.Equal([$"{Root}/2025/Keeper.pdf"], MirrorFiles());
        Assert.Equal(1, await StateRowCountAsync(sourceId));
    }

    // --- the circuit breaker ---

    [Fact]
    public async Task MassDeletion_TripsTheBreaker_SkippingDeletesButStillApplyingPuts()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var doomed = new List<Guid>();
        for (var i = 0; i < 30; i++)
        {
            var id = await UploadAndWaitAsync(client, $"bulk{i}.pdf");
            await RenameDocumentAsync(id, $"Bulk {i:D2}");
            await SetDocumentDateAsync(id, new DateOnly(2025, 1, 1));
            doomed.Add(id);
        }

        await RunBackupExpectingSuccessAsync(client, sourceId);
        Assert.Equal(30, MirrorFiles().Count);

        // 30 deletions clears both thresholds: > 25 absolute and > 10% of 30.
        (await client.PostAsJsonAsync("/api/v1/documents/bulk-delete", new { ids = doomed }, CT))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/documents/bulk-permanently-delete", new { ids = doomed }, CT))
            .EnsureSuccessStatusCode();

        // A new document alongside the mass deletion: blocking prunes protects data, but blocking puts
        // would lose it, so the run must still copy this one.
        var survivor = await UploadAndWaitAsync(client, "survivor.pdf");
        await RenameDocumentAsync(survivor, "Survivor");
        await SetDocumentDateAsync(survivor, new DateOnly(2026, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);

        var files = MirrorFiles();
        Assert.Contains($"{Root}/2026/Survivor.pdf", files);
        Assert.Equal(31, files.Count);

        // The latch lives on the pairing, not the destination.
        var source = await GetSourceAsync(client, sourceId);
        Assert.NotNull(source.PruneBlockedAt);
        Assert.Equal(30, source.PruneBlockedCount);

        // Prune anyway.
        await RunBackupExpectingSuccessAsync(client, sourceId, allowLargePrune: true);

        Assert.Equal([$"{Root}/2026/Survivor.pdf"], MirrorFiles());

        // And the banner comes down with it: the override carried out exactly the deletions the latch was
        // withholding, so leaving it up would report a block that no longer exists.
        var pruned = await GetSourceAsync(client, sourceId);
        Assert.Null(pruned.PruneBlockedAt);
        Assert.Null(pruned.PruneBlockedCount);
    }

    // A parent whose CHILDREN failed carries no error string of its own — the generic parent-progress
    // logic derives status from the child tally without writing one. Without surfacing the batch's
    // message, the admin page would show a red badge with no explanation of which file failed.
    [Fact]
    public async Task WhenABatchFails_TheApiSurfacesItsMessage()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "orphan.pdf");
        await RenameDocumentAsync(docId, "Orphan");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        // Delete the blob behind the app's back, leaving the version row intact. That is "the library
        // lost a file", not a deletion race, so the batch must fail rather than skip silently — a silent
        // skip writes no state row and re-plans the same copy on every future run.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.DocumentId == docId, CT);
            File.Delete(Path.Combine(Factory.MediaPath, version.FilePath));
        }

        var job = await RunBackupAsync(client, sourceId);
        Assert.Equal(ProcessingJobStatus.Failed, job.Status);

        var source = await GetSourceAsync(client, sourceId);

        Assert.Null(source.LastRun!.Error);
        var batchError = Assert.Single(source.LastRun.BatchErrors);
        Assert.Contains("missing from storage", batchError);
        Assert.Equal(0, source.BackedUpFileCount);
    }

    // The other half: a run that copies nothing must not leave a state row claiming it did, or the next
    // run would skip the file forever.
    [Fact]
    public async Task AFailedCopy_WritesNoStateRow()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "orphan.pdf");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.DocumentId == docId, CT);
            File.Delete(Path.Combine(Factory.MediaPath, version.FilePath));
        }

        await RunBackupAsync(client, sourceId);

        Assert.Equal(0, await StateRowCountAsync(sourceId));
        Assert.Empty(MirrorFiles());
    }

    // --- endpoints ---

    [Fact]
    public async Task DryRun_ReportsThePlanWithoutTouchingTheMirror()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "planned.pdf");
        await RenameDocumentAsync(docId, "Planned");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        var job = await RunBackupExpectingSuccessAsync(client, sourceId, dryRun: true);

        Assert.Equal(0, job.TotalChildCount);
        Assert.Empty(MirrorFiles());
        Assert.Equal(0, await StateRowCountAsync(sourceId));

        var source = await GetSourceAsync(client, sourceId);

        Assert.True(source.LastRun!.Summary!.DryRun);
        Assert.Equal(1, source.LastRun.Summary.Puts);
        Assert.Equal(0, source.BackedUpFileCount);
    }

    [Fact]
    public async Task RunNow_ReturnsAJobId_AndASecondCallIs409()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        await UploadAndWaitAsync(client, "queued.pdf");

        // Hold the worker so the first run stays queued and the single-flight guard is observable.
        await using var _ = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);

        var first = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT);
        first.EnsureSuccessStatusCode();

        var body = await first.Content.ReadFromJsonAsync<RunResult>(CT);
        Assert.NotEqual(Guid.Empty, body!.JobId);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // The single-flight filter has to exclude batch CHILDREN, which carry their parent's EntityId —
    // otherwise the second run after any real run would 409 forever.
    [Fact]
    public async Task RunNow_AfterACompletedRunWithChildren_IsNotBlockedByThem()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "again.pdf");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT);

        second.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetTargets_ReportsConfigAndPerSourceCopyTotals()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var docId = await UploadAndWaitAsync(client, "counted.pdf");
        await RenameDocumentAsync(docId, "Counted");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client, sourceId);

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var target = LocalTarget(targets!);

        Assert.Equal(TargetKey, target.Key);
        Assert.Equal("Local", target.Kind);
        Assert.True(target.IsAvailable);
        Assert.Null(target.ConfigurationError);
        Assert.True(target.IsEnabled);

        var source = Assert.Single(target.Sources);
        Assert.Equal(DocumentsBackupSourceDescriptor.SourceKey, source.SourceKind);
        Assert.Equal("Documents", source.SourceDisplayName);
        Assert.True(source.IsEnabled);
        Assert.True(source.IsAvailable);
        Assert.True(source.SupportsPrune);
        Assert.Equal(1, source.BackedUpFileCount);
        Assert.True(source.BackedUpBytes > 0);
        Assert.Equal("Completed", source.LastRun!.Status);

        // The picker's feed rides along with the list, so "what can I add" needs no second round trip.
        Assert.Contains(targets.SourceKinds, k => k.Key == DocumentsBackupSourceDescriptor.SourceKey);
    }

    // The catch-all staleness line renders "never" off a NULL, so a pairing that has never completed a
    // run must send null — not the default DateTimeOffset, which reads as a date two millennia ago.
    [Fact]
    public async Task LastSuccessfulRunAt_IsNullUntilARunActuallyCompletes()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        Assert.Null((await GetSourceAsync(client, sourceId)).LastSuccessfulRunAt);

        await RunBackupExpectingSuccessAsync(client, sourceId);

        var after = (await GetSourceAsync(client, sourceId)).LastSuccessfulRunAt;
        Assert.NotNull(after);
        Assert.True(after > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task DisablingTheTarget_BlocksRunsForEverySourceOnIt()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}", new { isEnabled = false }, CT);
        patch.EnsureSuccessStatusCode();

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        Assert.False(LocalTarget(targets!).IsEnabled);

        var run = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, run.StatusCode);
    }

    [Fact]
    public async Task UnknownTarget_Is404()
    {
        var client = await CreateAdminClientAsync();

        var patch = await client.PatchAsJsonAsync(
            "/api/v1/admin/backup/targets/nope", new { isEnabled = false }, CT);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task UnknownSource_Is404()
    {
        var client = await CreateAdminClientAsync();

        var run = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{Guid.NewGuid()}/runs", new { }, CT);
        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
    }

    [Fact]
    public async Task ScheduleUpsertAndDelete_RoundTrip()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var upsert = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);
        upsert.EnsureSuccessStatusCode();

        var schedule = (await GetSourceAsync(client, sourceId)).Schedule;
        Assert.NotNull(schedule);
        Assert.Equal("0 3 * * *", schedule.CronExpression);

        // Upsert updates in place rather than replacing: the id is a foreign key on the source row and is
        // referenced by every job the schedule spawned.
        var second = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "30 4 * * *", isEnabled = true, priority = 0 }, CT);
        second.EnsureSuccessStatusCode();

        var updated = (await GetSourceAsync(client, sourceId)).Schedule;
        Assert.NotNull(updated);
        Assert.Equal(schedule.Id, updated.Id);
        Assert.Equal("30 4 * * *", updated.CronExpression);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.ProcessingSchedules
                .CountAsync(s => s.JobType == ProcessingJobType.BackupToTarget, CT);
            Assert.Equal(1, count);
        }

        var delete = await client.DeleteAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule", CT);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var deleteAgain = await client.DeleteAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule", CT);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task InvalidCron_Is400()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "not a cron", isEnabled = true, priority = 0 }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BackupEndpoints_RequireAdmin()
    {
        var admin = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(admin);

        var contributor = await CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await contributor.GetAsync("/api/v1/admin/backup/targets", CT)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await contributor.PostAsJsonAsync(
                $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await contributor.DeleteAsync($"/api/v1/admin/backup/sources/{sourceId}", CT)).StatusCode);

        var anonymous = CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/admin/backup/targets", CT)).StatusCode);
    }

    private record SourceResult(Guid Id, string SourceKind, string TargetKey);

    private record RunResult(Guid JobId);

    private record TargetsResult(List<TargetItem> Items, List<SourceKindItem> SourceKinds);

    private record SourceKindItem(string Key, string DisplayName, bool SupportsPrune);

    private record TargetItem(
        string Key,
        string? Kind,
        string? Location,
        bool IsAvailable,
        string? ConfigurationError,
        bool IsEnabled,
        List<SourceItem> Sources);

    private record SourceItem(
        Guid Id,
        string SourceKind,
        string? SourceDisplayName,
        bool IsEnabled,
        bool IsAvailable,
        string? UnavailableReason,
        bool SupportsPrune,
        DateTimeOffset? PruneBlockedAt,
        int? PruneBlockedCount,
        int BackedUpFileCount,
        long BackedUpBytes,
        RunStatusItem? LastRun,
        DateTimeOffset? LastSuccessfulRunAt,
        ScheduleItem? Schedule);

    private record RunStatusItem(
        Guid JobId,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        string? Error,
        List<string> BatchErrors,
        int TotalChildCount,
        int CompletedChildCount,
        int FailedChildCount,
        RunSummaryItem? Summary);

    private record RunSummaryItem(
        Guid BackupSourceId,
        bool DryRun,
        bool Skipped,
        string? SkipReason,
        int Puts,
        int Moves,
        int Deletes,
        int Unchanged,
        int BatchCount,
        bool PruneBlocked,
        int BlockedDeleteCount,
        long? TotalBytes);

    private record ScheduleItem(
        Guid Id, string CronExpression, bool IsEnabled, int Priority, DateTimeOffset NextRunAt);
}
