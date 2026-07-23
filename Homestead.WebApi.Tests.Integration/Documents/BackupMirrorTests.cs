using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

/// <summary>
/// End-to-end coverage of the one-way document backup mirror against a real filesystem target: the
/// planner's layout, the incremental diff (put / move / delete / no-op), the bulk-delete circuit
/// breaker, and the admin endpoints.
/// </summary>
[Collection("Integration")]
public class BackupMirrorTests : IntegrationTestBase
{
    private const string TargetKey = HomesteadApiFactory.BackupTargetKey;

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

    /// <summary>Runs a full backup and waits for the parent and every batch child to terminate.</summary>
    private async Task<ProcessingJob> RunBackupAsync(
        HttpClient client, bool dryRun = false, bool allowLargePrune = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/runs",
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
                or ProcessingJobStatus.Failed or ProcessingJobStatus.PartiallyCompleted)
            {
                return job;
            }

            await Task.Delay(150, CT);
        }

        throw new TimeoutException($"Backup job {jobId} did not reach a terminal status within timeout");
    }

    private async Task<ProcessingJob> RunBackupExpectingSuccessAsync(
        HttpClient client, bool dryRun = false, bool allowLargePrune = false)
    {
        var job = await RunBackupAsync(client, dryRun, allowLargePrune);
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

    private async Task<int> StateRowCountAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DocumentBackupStates.CountAsync(s => s.TargetKey == TargetKey, CT);
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

        var dated = await UploadAndWaitAsync(client, "dated.pdf");
        await RenameDocumentAsync(dated, "Tax Return 2024");
        await SetDocumentDateAsync(dated, new DateOnly(2024, 4, 15));

        var undated = await UploadAndWaitAsync(client, "undated.pdf");
        await RenameDocumentAsync(undated, "Misc Receipt");

        await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(
            ["2024/Tax Return 2024.pdf", "_undated/Misc Receipt.pdf"],
            MirrorFiles());
    }

    [Fact]
    public async Task MultipleVersions_LatestAtShardRoot_SupersededUnderVersionsFolder()
    {
        var client = await CreateAdminClientAsync();

        var docId = await UploadAndWaitAsync(client, "lease.pdf");
        await RenameDocumentAsync(docId, "Lease");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));
        await AddVersionAndWaitAsync(client, docId);

        await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(
            ["2025/Lease.pdf", "2025/_versions/Lease (v1).pdf"],
            MirrorFiles());
    }

    [Fact]
    public async Task MirroredBytesMatchTheOriginal()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "payload.pdf");
        await RenameDocumentAsync(docId, "Payload");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.DocumentId == docId, CT);

        var original = await File.ReadAllBytesAsync(
            Path.Combine(Factory.MediaPath, version.FilePath), CT);
        var mirrored = await File.ReadAllBytesAsync(
            Path.Combine(MirrorRoot, "2025", "Payload.pdf"), CT);

        Assert.Equal(original, mirrored);
    }

    // --- the incremental diff ---

    [Fact]
    public async Task SecondRunWithNoChanges_DoesNothing()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "stable.pdf");
        await RenameDocumentAsync(docId, "Stable");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);
        var writtenAt = File.GetLastWriteTimeUtc(Path.Combine(MirrorRoot, "2025", "Stable.pdf"));

        var second = await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(0, second.TotalChildCount);
        Assert.Equal(["2025/Stable.pdf"], MirrorFiles());
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(Path.Combine(MirrorRoot, "2025", "Stable.pdf")));
    }

    [Fact]
    public async Task RenamingADocument_MovesTheFileRatherThanDuplicatingIt()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "before.pdf");
        await RenameDocumentAsync(docId, "Before");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);
        Assert.Equal(["2025/Before.pdf"], MirrorFiles());

        await RenameDocumentAsync(docId, "After");
        await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(["2025/After.pdf"], MirrorFiles());
        Assert.Equal(1, await StateRowCountAsync());
    }

    [Fact]
    public async Task ChangingTheDocumentDate_ReShardsEveryVersion()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "shifting.pdf");
        await RenameDocumentAsync(docId, "Shifting");
        await SetDocumentDateAsync(docId, new DateOnly(2024, 6, 1));
        await AddVersionAndWaitAsync(client, docId);

        await RunBackupExpectingSuccessAsync(client);
        Assert.Equal(
            ["2024/Shifting.pdf", "2024/_versions/Shifting (v1).pdf"],
            MirrorFiles());

        await SetDocumentDateAsync(docId, new DateOnly(2026, 6, 1));
        await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(
            ["2026/Shifting.pdf", "2026/_versions/Shifting (v1).pdf"],
            MirrorFiles());
    }

    [Fact]
    public async Task AddingAVersion_DemotesTheOldLatestIntoTheVersionsFolder()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "growing.pdf");
        await RenameDocumentAsync(docId, "Growing");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);
        Assert.Equal(["2025/Growing.pdf"], MirrorFiles());

        await AddVersionAndWaitAsync(client, docId);
        await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(
            ["2025/Growing.pdf", "2025/_versions/Growing (v1).pdf"],
            MirrorFiles());
        Assert.Equal(2, await StateRowCountAsync());
    }

    // Trash is a restorable state — a backup that evaporated the moment a file hit the trash would be
    // exactly the copy you needed.
    [Fact]
    public async Task TrashingADocument_LeavesTheMirrorUntouched()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "doomed.pdf");
        await RenameDocumentAsync(docId, "Doomed");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);
        Assert.Equal(["2025/Doomed.pdf"], MirrorFiles());

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/{docId}", CT);
        deleteResponse.EnsureSuccessStatusCode();

        var second = await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(0, second.TotalChildCount);
        Assert.Equal(["2025/Doomed.pdf"], MirrorFiles());
    }

    [Fact]
    public async Task PermanentlyDeletingADocument_RemovesItFromTheMirror()
    {
        var client = await CreateAdminClientAsync();
        var keeper = await UploadAndWaitAsync(client, "keeper.pdf");
        await RenameDocumentAsync(keeper, "Keeper");
        await SetDocumentDateAsync(keeper, new DateOnly(2025, 1, 1));

        var doomed = await UploadAndWaitAsync(client, "doomed.pdf");
        await RenameDocumentAsync(doomed, "Doomed");
        await SetDocumentDateAsync(doomed, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);
        Assert.Equal(["2025/Doomed.pdf", "2025/Keeper.pdf"], MirrorFiles());

        (await client.DeleteAsync($"/api/v1/documents/{doomed}", CT)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/documents/{doomed}/permanently-delete", null, CT))
            .EnsureSuccessStatusCode();

        await RunBackupExpectingSuccessAsync(client);

        Assert.Equal(["2025/Keeper.pdf"], MirrorFiles());
        Assert.Equal(1, await StateRowCountAsync());
    }

    // --- the circuit breaker ---

    [Fact]
    public async Task MassDeletion_TripsTheBreaker_SkippingDeletesButStillApplyingPuts()
    {
        var client = await CreateAdminClientAsync();

        var doomed = new List<Guid>();
        for (var i = 0; i < 30; i++)
        {
            var id = await UploadAndWaitAsync(client, $"bulk{i}.pdf");
            await RenameDocumentAsync(id, $"Bulk {i:D2}");
            await SetDocumentDateAsync(id, new DateOnly(2025, 1, 1));
            doomed.Add(id);
        }

        await RunBackupExpectingSuccessAsync(client);
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

        await RunBackupExpectingSuccessAsync(client);

        var files = MirrorFiles();
        Assert.Contains("2026/Survivor.pdf", files);
        Assert.Equal(31, files.Count);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.BackupTargetStates.AsNoTracking()
                .SingleAsync(s => s.TargetKey == TargetKey, CT);
            Assert.NotNull(state.PruneBlockedAt);
            Assert.Equal(30, state.PruneBlockedCount);
        }

        // Prune anyway.
        await RunBackupExpectingSuccessAsync(client, allowLargePrune: true);

        Assert.Equal(["2026/Survivor.pdf"], MirrorFiles());
    }

    // A parent whose CHILDREN failed carries no error string of its own — the generic parent-progress
    // logic derives status from the child tally without writing one. Without surfacing the batch's
    // message, the admin page would show a red badge with no explanation of which file failed.
    [Fact]
    public async Task WhenABatchFails_TheApiSurfacesItsMessage()
    {
        var client = await CreateAdminClientAsync();
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

        var job = await RunBackupAsync(client);
        Assert.Equal(ProcessingJobStatus.Failed, job.Status);

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var target = Assert.Single(targets!.Items);

        Assert.Null(target.LastRun!.Error);
        var batchError = Assert.Single(target.LastRun.BatchErrors);
        Assert.Contains("missing from storage", batchError);
        Assert.Equal(0, target.BackedUpFileCount);
    }

    // The other half: a run that copies nothing must not leave a state row claiming it did, or the next
    // run would skip the file forever.
    [Fact]
    public async Task AFailedCopy_WritesNoStateRow()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "orphan.pdf");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.DocumentId == docId, CT);
            File.Delete(Path.Combine(Factory.MediaPath, version.FilePath));
        }

        await RunBackupAsync(client);

        Assert.Equal(0, await StateRowCountAsync());
        Assert.Empty(MirrorFiles());
    }

    // --- endpoints ---

    [Fact]
    public async Task DryRun_ReportsThePlanWithoutTouchingTheMirror()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "planned.pdf");
        await RenameDocumentAsync(docId, "Planned");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        var job = await RunBackupExpectingSuccessAsync(client, dryRun: true);

        Assert.Equal(0, job.TotalChildCount);
        Assert.Empty(MirrorFiles());
        Assert.Equal(0, await StateRowCountAsync());

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var target = Assert.Single(targets!.Items);

        Assert.True(target.LastRun!.Summary!.DryRun);
        Assert.Equal(1, target.LastRun.Summary.PutCount);
        Assert.Equal(0, target.BackedUpFileCount);
    }

    [Fact]
    public async Task RunNow_ReturnsAJobId_AndASecondCallIs409()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "queued.pdf");

        // Hold the worker so the first run stays queued and the single-flight guard is observable.
        await using var _ = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);

        var first = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/runs", new { }, CT);
        first.EnsureSuccessStatusCode();

        var body = await first.Content.ReadFromJsonAsync<RunResult>(CT);
        Assert.NotEqual(Guid.Empty, body!.JobId);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/runs", new { }, CT);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task GetTargets_ReportsConfigAndCopyTotals()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "counted.pdf");
        await RenameDocumentAsync(docId, "Counted");
        await SetDocumentDateAsync(docId, new DateOnly(2025, 1, 1));

        await RunBackupExpectingSuccessAsync(client);

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var target = Assert.Single(targets!.Items);

        Assert.Equal(TargetKey, target.Key);
        Assert.Equal("Local", target.Kind);
        Assert.True(target.IsAvailable);
        Assert.Null(target.ConfigurationError);
        Assert.True(target.IsEnabled);
        Assert.Equal(1, target.BackedUpFileCount);
        Assert.True(target.BackedUpBytes > 0);
        Assert.Equal("Completed", target.LastRun!.Status);
    }

    [Fact]
    public async Task PatchTarget_DisablesRunsAndClearsThePruneLatch()
    {
        var client = await CreateAdminClientAsync();

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BackupTargetStates.Add(new BackupTargetState
            {
                TargetKey = TargetKey,
                IsEnabled = true,
                PruneBlockedAt = DateTimeOffset.UtcNow,
                PruneBlockedCount = 42,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(CT);
        }

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}", new { isEnabled = false }, CT);
        patch.EnsureSuccessStatusCode();

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var target = Assert.Single(targets!.Items);

        Assert.False(target.IsEnabled);
        Assert.Null(target.PruneBlockedAt);
        Assert.Null(target.PruneBlockedCount);

        var run = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/runs", new { }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, run.StatusCode);
    }

    [Fact]
    public async Task UnknownTarget_Is404()
    {
        var client = await CreateAdminClientAsync();

        var run = await client.PostAsJsonAsync("/api/v1/admin/backup/targets/nope/runs", new { }, CT);
        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);

        var patch = await client.PatchAsJsonAsync(
            "/api/v1/admin/backup/targets/nope", new { isEnabled = false }, CT);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task ScheduleUpsertAndDelete_RoundTrip()
    {
        var client = await CreateAdminClientAsync();

        var upsert = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);
        upsert.EnsureSuccessStatusCode();

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var schedule = Assert.Single(targets!.Items).Schedule;
        Assert.NotNull(schedule);
        Assert.Equal("0 3 * * *", schedule.CronExpression);

        // Upsert replaces rather than accumulating.
        var second = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/schedule",
            new { cronExpression = "30 4 * * *", isEnabled = true, priority = 0 }, CT);
        second.EnsureSuccessStatusCode();

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.ProcessingSchedules
                .CountAsync(s => s.JobType == ProcessingJobType.BackupToTarget, CT);
            Assert.Equal(1, count);
        }

        var delete = await client.DeleteAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/schedule", CT);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var deleteAgain = await client.DeleteAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/schedule", CT);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task InvalidCron_Is400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/schedule",
            new { cronExpression = "not a cron", isEnabled = true, priority = 0 }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BackupEndpoints_RequireAdmin()
    {
        var contributor = await CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await contributor.GetAsync("/api/v1/admin/backup/targets", CT)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await contributor.PostAsJsonAsync(
                $"/api/v1/admin/backup/targets/{TargetKey}/runs", new { }, CT)).StatusCode);

        var anonymous = CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/admin/backup/targets", CT)).StatusCode);
    }

    private record RunResult(Guid JobId);

    private record TargetsResult(List<TargetItem> Items);

    private record TargetItem(
        string Key,
        string? Kind,
        string? Location,
        bool IsAvailable,
        string? ConfigurationError,
        bool IsEnabled,
        DateTimeOffset? PruneBlockedAt,
        int? PruneBlockedCount,
        int BackedUpFileCount,
        long BackedUpBytes,
        RunStatusItem? LastRun,
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
        string TargetKey,
        bool DryRun,
        bool Skipped,
        string? SkipReason,
        int PutCount,
        int MoveCount,
        int DeleteCount,
        int UnchangedCount,
        int BatchCount,
        long TotalBytes,
        bool PruneBlocked,
        int BlockedDeleteCount);

    private record ScheduleItem(
        Guid Id, string CronExpression, bool IsEnabled, int Priority, DateTimeOffset NextRunAt);
}
