using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Admin;

/// <summary>
/// The backup pairing's own lifecycle: creating it, the uniqueness invariant that keeps two same-kind
/// pairings off one target, the latch-acknowledgement split between source and target, and
/// cancel-then-delete. The mirroring itself lives in <c>BackupMirrorTests</c>.
/// </summary>
[Collection("Integration")]
public class BackupSourceApiTests : IntegrationTestBase
{
    private const string TargetKey = HomesteadApiFactory.BackupTargetKey;
    private const string SourceKind = DocumentsBackupSourceDescriptor.SourceKey;

    public BackupSourceApiTests(PostgresFixture fixture) : base(fixture) { }

    // --- helpers ---

    private static object CreateBody(string? sourceKind = null, string? targetKey = null) =>
        new { sourceKind = sourceKind ?? SourceKind, targetKey = targetKey ?? TargetKey };

    private async Task<Guid> CreateSourceAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/backup/sources", CreateBody(), CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SourceResult>(CT);
        return body!.Id;
    }

    private async Task<SourceItem> GetSourceAsync(HttpClient client, Guid sourceId)
    {
        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        return targets!.Items.SelectMany(t => t.Sources).Single(s => s.Id == sourceId);
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Stages a Pending parent run plus one Pending child, as a queued run would look.</summary>
    private async Task<(Guid ParentId, Guid ChildId)> SeedQueuedRunAsync(Guid sourceId, int totalChildren = 1)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parent = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupToTarget,
            Status = ProcessingJobStatus.Pending,
            EntityType = ProcessingEntityType.BackupSource,
            EntityId = sourceId,
            TotalChildCount = totalChildren,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var child = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BackupBatch,
            Status = ProcessingJobStatus.Pending,
            EntityType = ProcessingEntityType.BackupSource,
            EntityId = sourceId,
            ParentJobId = parent.Id,
            Priority = -100,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ProcessingJobs.AddRange(parent, child);
        await db.SaveChangesAsync(CT);

        return (parent.Id, child.Id);
    }

    // --- create ---

    [Fact]
    public async Task Create_PersistsTheCanonicalTargetCasing()
    {
        var client = await CreateAdminClientAsync();

        // The caller shouts the key; the row stores what the resolver calls it, so the row and config
        // never disagree about how the target is spelled.
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/backup/sources", CreateBody(targetKey: TargetKey.ToUpperInvariant()), CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SourceResult>(CT);
        Assert.Equal(TargetKey, body!.TargetKey);

        var stored = await WithDbAsync(db =>
            db.BackupSources.AsNoTracking().SingleAsync(s => s.Id == body.Id, CT));
        Assert.Equal(TargetKey, stored.TargetKey);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task Create_Duplicate_Is409()
    {
        var client = await CreateAdminClientAsync();
        await CreateSourceAsync(client);

        var duplicate = await client.PostAsJsonAsync("/api/v1/admin/backup/sources", CreateBody(), CT);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    // Config keys compare case-insensitively, so "Local" and "local" name ONE target — two rows for them
    // would share a root folder and prune each other's files by anti-join.
    [Fact]
    public async Task Create_DuplicateDifferingOnlyInCase_Is409()
    {
        var client = await CreateAdminClientAsync();
        await CreateSourceAsync(client);

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/admin/backup/sources", CreateBody(targetKey: "LOCAL"), CT);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    // The handler's pre-check is racy on its own; the expression unique index is the actual guarantee.
    // Asserted by going around the API entirely — a fluent (case-sensitive) index would let this through.
    [Fact]
    public async Task TheUniqueIndexRejectsACaseVariantInsertedDirectly()
    {
        var client = await CreateAdminClientAsync();
        await CreateSourceAsync(client);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.BackupSources.Add(new BackupSource
        {
            Id = Guid.NewGuid(),
            SourceKind = SourceKind,
            TargetKey = "LOCAL",
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(CT));
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }

    [Fact]
    public async Task Create_UnknownSourceKind_Is400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/backup/sources", CreateBody(sourceKind: "finances"), CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UndefinedTarget_Is404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/backup/sources", CreateBody(targetKey: "nope"), CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WritesAnAuditRow()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var audited = await WithDbAsync(db => db.AuditLogs.AsNoTracking().AnyAsync(
            a => a.EntityType == "BackupSource" && a.EntityId == sourceId
                 && a.Action == AuditAction.Created, CT));

        Assert.True(audited);
    }

    // --- patch ---

    [Fact]
    public async Task Patch_DisablesTheSource_AndBlocksRunNow()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}", new { isEnabled = false }, CT);
        patch.EnsureSuccessStatusCode();

        Assert.False((await GetSourceAsync(client, sourceId)).IsEnabled);

        var run = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, run.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAcknowledgePruneBlock_ClearsTheLatch()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);
        await LatchAsync(sourceId);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}", new { acknowledgePruneBlock = true }, CT);
        patch.EnsureSuccessStatusCode();

        var source = await GetSourceAsync(client, sourceId);
        Assert.Null(source.PruneBlockedAt);
        Assert.Null(source.PruneBlockedCount);
    }

    // A plain enable/disable toggle must NOT clear the latch: the banner is the only place a blocked
    // prune is reported, so dismissing it has to be a deliberate act of its own.
    [Fact]
    public async Task Patch_WithoutTheAckFlag_LeavesTheLatchAlone()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);
        await LatchAsync(sourceId);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}", new { isEnabled = false }, CT);
        patch.EnsureSuccessStatusCode();

        var source = await GetSourceAsync(client, sourceId);
        Assert.NotNull(source.PruneBlockedAt);
        Assert.Equal(7, source.PruneBlockedCount);
    }

    // Clearing latches from the TARGET would dismiss every kind's warning on that destination at once.
    [Fact]
    public async Task PatchingTheTarget_DoesNotClearASourcesLatch()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);
        await LatchAsync(sourceId);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}", new { isEnabled = true }, CT);
        patch.EnsureSuccessStatusCode();

        Assert.NotNull((await GetSourceAsync(client, sourceId)).PruneBlockedAt);
    }

    [Fact]
    public async Task Patch_WritesAnAuditRow()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        (await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}", new { isEnabled = false }, CT))
            .EnsureSuccessStatusCode();

        var audit = await WithDbAsync(db => db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(
            a => a.EntityType == "BackupSource" && a.EntityId == sourceId
                 && a.Action == AuditAction.Updated, CT));

        Assert.NotNull(audit);
        Assert.Contains("IsEnabled", audit!.Changes);
    }

    [Fact]
    public async Task Patch_UnknownSource_Is404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/sources/{Guid.NewGuid()}", new { isEnabled = false }, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task LatchAsync(Guid sourceId, int blockedCount = 7)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.BackupSources.SingleAsync(s => s.Id == sourceId, CT);
        source.PruneBlockedAt = DateTimeOffset.UtcNow;
        source.PruneBlockedCount = blockedCount;
        await db.SaveChangesAsync(CT);
    }

    // --- schedule ---

    [Fact]
    public async Task ScheduleUpsertTwice_KeepsTheSameId()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var first = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<ScheduleResult>(CT);

        var second = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 4 * * *", isEnabled = false, priority = 5 }, CT);
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<ScheduleResult>(CT);

        Assert.Equal(firstBody!.Id, secondBody!.Id);

        var stored = await WithDbAsync(db =>
            db.BackupSources.AsNoTracking().SingleAsync(s => s.Id == sourceId, CT));
        Assert.Equal(firstBody.Id, stored.ScheduleId);
    }

    // "Why did backups stop running on the 3rd" has to be answerable. The cron lives on the schedule
    // row, so a snapshot diff of the source alone would come back empty for an in-place edit — and an
    // empty diff is dropped, losing exactly the change being asked about.
    [Fact]
    public async Task ArmingAndDisarmingTheSchedule_IsAudited()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        (await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT))
            .EnsureSuccessStatusCode();

        // Re-saving a different cron keeps the schedule id, so only the described value distinguishes it.
        (await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "30 4 * * *", isEnabled = true, priority = 0 }, CT))
            .EnsureSuccessStatusCode();

        (await client.DeleteAsync($"/api/v1/admin/backup/sources/{sourceId}/schedule", CT))
            .EnsureSuccessStatusCode();

        var changes = await WithDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "BackupSource" && a.EntityId == sourceId
                        && a.Action == AuditAction.Updated)
            .OrderBy(a => a.Timestamp)
            .Select(a => a.Changes!)
            .ToListAsync(CT));

        Assert.Equal(3, changes.Count);
        Assert.Contains("none", changes[0]);
        Assert.Contains("0 3 * * *", changes[0]);
        Assert.Contains("30 4 * * *", changes[1]);
        Assert.Contains("none", changes[2]);
    }

    // A disabled pairing may still be scheduled — arming the cron before switching the backup on is a
    // reasonable order to work in, and the planner skips a disabled source quietly.
    [Fact]
    public async Task ScheduleUpsert_OnADisabledSource_IsAllowed()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        (await client.PatchAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}", new { isEnabled = false }, CT))
            .EnsureSuccessStatusCode();

        var upsert = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);

        upsert.EnsureSuccessStatusCode();
    }

    // Deleting the schedule out of band must degrade the pairing to manual-only, never delete it.
    [Fact]
    public async Task DeletingTheScheduleRowDirectly_NullsTheSourcesScheduleId()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var upsert = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);
        upsert.EnsureSuccessStatusCode();
        var scheduleId = (await upsert.Content.ReadFromJsonAsync<ScheduleResult>(CT))!.Id;

        (await client.DeleteAsync($"/api/v1/admin/schedules/{scheduleId}", CT))
            .EnsureSuccessStatusCode();

        var stored = await WithDbAsync(db =>
            db.BackupSources.AsNoTracking().SingleAsync(s => s.Id == sourceId, CT));

        Assert.Null(stored.ScheduleId);
    }

    // --- delete ---

    [Fact]
    public async Task Delete_RemovesTheRow_ItsStateRows_AndItsSchedule()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        var upsert = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);
        upsert.EnsureSuccessStatusCode();
        var scheduleId = (await upsert.Content.ReadFromJsonAsync<ScheduleResult>(CT))!.Id;

        // A job that ran from the schedule: its history must survive with a SET NULL'd ScheduleId.
        Guid jobId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.DocumentBackupStates.Add(new Homestead.WebApi.Domain.Backup.DocumentBackupState
            {
                Id = Guid.NewGuid(),
                BackupSourceId = sourceId,
                DocumentVersionId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                RemoteRelativePath = "Documents/2025/Doc.pdf",
                BackedUpHash = new string('a', 64),
                FileSize = 10,
                BackedUpAt = DateTimeOffset.UtcNow,
            });

            var job = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupToTarget,
                Status = ProcessingJobStatus.Completed,
                EntityType = ProcessingEntityType.BackupSource,
                EntityId = sourceId,
                ScheduleId = scheduleId,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            db.ProcessingJobs.Add(job);
            jobId = job.Id;

            await db.SaveChangesAsync(CT);
        }

        var delete = await client.DeleteAsync($"/api/v1/admin/backup/sources/{sourceId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.False(await db.BackupSources.AnyAsync(s => s.Id == sourceId, CT));
            // Cascade: deleting a pairing is an explicit "forget this backup".
            Assert.False(await db.DocumentBackupStates.AnyAsync(s => s.BackupSourceId == sourceId, CT));
            Assert.False(await db.ProcessingSchedules.AnyAsync(s => s.Id == scheduleId, CT));

            // The job row survives, with its schedule reference nulled rather than cascaded away.
            var job = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, CT);
            Assert.Null(job.ScheduleId);
        }

        var audited = await WithDbAsync(db => db.AuditLogs.AsNoTracking().AnyAsync(
            a => a.EntityType == "BackupSource" && a.EntityId == sourceId
                 && a.Action == AuditAction.Deleted, CT));
        Assert.True(audited);
    }

    [Fact]
    public async Task Delete_WithALiveRunAndNoFlag_Is409()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        // Hold the worker so the seeded run cannot be claimed out from under the test.
        await using var _ = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);
        await SeedQueuedRunAsync(sourceId);

        var delete = await client.DeleteAsync($"/api/v1/admin/backup/sources/{sourceId}", CT);

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.True(await WithDbAsync(db => db.BackupSources.AnyAsync(s => s.Id == sourceId, CT)));
    }

    [Fact]
    public async Task Delete_WithCancelRun_CancelsParentAndChildrenThenDeletes()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        await using var _ = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);
        var (parentId, childId) = await SeedQueuedRunAsync(sourceId);

        var delete = await client.DeleteAsync(
            $"/api/v1/admin/backup/sources/{sourceId}?cancelRun=true", CT);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parent = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == parentId, CT);
        var child = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == childId, CT);

        Assert.Equal(ProcessingJobStatus.Cancelled, parent.Status);
        Assert.NotNull(parent.CompletedAt);
        Assert.Equal(ProcessingJobStatus.Cancelled, child.Status);

        Assert.False(await db.BackupSources.AnyAsync(s => s.Id == sourceId, CT));
    }

    // The planner is executing right now (InProgress parent that has not yet reported a child count), so
    // it is about to insert children referencing the row we would delete. Refuse and say so.
    [Fact]
    public async Task Delete_WhileThePlannerIsRunning_Is409AndRollsTheCancellationBack()
    {
        var client = await CreateAdminClientAsync();
        var sourceId = await CreateSourceAsync(client);

        await using var _ = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);

        Guid parentId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parent = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupToTarget,
                Status = ProcessingJobStatus.InProgress,
                EntityType = ProcessingEntityType.BackupSource,
                EntityId = sourceId,
                TotalChildCount = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = DateTimeOffset.UtcNow,
            };
            db.ProcessingJobs.Add(parent);
            await db.SaveChangesAsync(CT);
            parentId = parent.Id;
        }

        var delete = await client.DeleteAsync(
            $"/api/v1/admin/backup/sources/{sourceId}?cancelRun=true", CT);

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

        using var verify = CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        // The 409 rolled the whole command back — the run is untouched, so a retry starts clean.
        var parentAfter = await verifyDb.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == parentId, CT);
        Assert.Equal(ProcessingJobStatus.InProgress, parentAfter.Status);
        Assert.True(await verifyDb.BackupSources.AnyAsync(s => s.Id == sourceId, CT));
    }

    [Fact]
    public async Task Delete_UnknownSource_Is404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.DeleteAsync(
            $"/api/v1/admin/backup/sources/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record SourceResult(Guid Id, string SourceKind, string TargetKey);

    private record ScheduleResult(Guid Id, DateTimeOffset NextRunAt);

    private record TargetsResult(List<TargetItem> Items, List<SourceKindItem> SourceKinds);

    private record SourceKindItem(string Key, string DisplayName, bool SupportsPrune);

    private record TargetItem(string Key, bool IsAvailable, bool IsEnabled, List<SourceItem> Sources);

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
        long BackedUpBytes);
}
