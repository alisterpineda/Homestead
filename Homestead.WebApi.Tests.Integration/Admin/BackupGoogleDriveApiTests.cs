using System.Net;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Modules.Documents.Backup;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Admin;

/// <summary>
/// The Google Drive target's credential lifecycle, over the real endpoints, the real grant store, real
/// Data Protection, and a real database. Only Google's own two HTTP surfaces are faked.
/// </summary>
[Collection("Integration")]
public class BackupGoogleDriveApiTests : IntegrationTestBase
{
    private const string TargetKey = HomesteadApiFactory.GoogleDriveTargetKey;
    private const string SourceKind = DocumentsBackupSourceDescriptor.SourceKey;

    public BackupGoogleDriveApiTests(PostgresFixture fixture) : base(fixture) { }

    private FakeGoogleOAuthClient Oauth => Factory.GoogleOAuth;
    private FakeGoogleDriveAccountClient Accounts => Factory.GoogleAccounts;

    // --- helpers --------------------------------------------------------------------------------------

    private async Task<TargetItem> GetTargetAsync(HttpClient client)
    {
        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        return targets!.Items.Single(t => t.Key == TargetKey);
    }

    /// <summary>Drives connect → poll to completion, which is how every "already connected" test starts.</summary>
    private async Task<ConnectResult> ConnectAsync(HttpClient client, string email = "owner@example.test")
    {
        Accounts.NextAccount = new GoogleDriveAccountInfo(email, 1_000, 15_000_000_000);

        var start = await client.PostAsync($"/api/v1/admin/backup/targets/{TargetKey}/connect", null, CT);
        start.EnsureSuccessStatusCode();

        var connect = (await start.Content.ReadFromJsonAsync<ConnectResult>(CT))!;

        var poll = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connect/poll",
            new { pollToken = connect.PollToken }, CT);
        poll.EnsureSuccessStatusCode();

        return connect with { Poll = (await poll.Content.ReadFromJsonAsync<PollResult>(CT))! };
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Guid> CreatePairingAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/backup/sources",
            new { sourceKind = SourceKind, targetKey = TargetKey }, CT);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SourceResult>(CT))!.Id;
    }

    private async Task ResetAsync()
    {
        Oauth.Reset();
        Accounts.Reset();

        // The grant lives in the database, and Respawn does not know about the singleton token cache.
        using var scope = CreateScope();
        scope.ServiceProvider.GetRequiredService<GoogleDriveTokenManager>().Invalidate(TargetKey);
    }

    // --- The card, before anything is connected -------------------------------------------------------

    [Fact]
    public async Task AConfiguredButUnconnectedTarget_RendersUnavailableWithAConnectPrompt()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        var target = await GetTargetAsync(client);

        Assert.Equal("GoogleDrive", target.Kind);
        Assert.Contains("Google Drive", target.Location);

        // Config is perfect; the credential is what is missing, and the card has to say which.
        Assert.False(target.IsAvailable);
        Assert.Contains("Not connected to Google", target.ConfigurationError);

        Assert.NotNull(target.Connection);
        Assert.False(target.Connection!.IsConnected);
        Assert.Null(target.Connection.ConnectedAccountEmail);
    }

    // A local directory has nothing to connect; the section must not render for it at all.
    [Fact]
    public async Task ALocalTarget_CarriesNoConnectionSection()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        var targets = await client.GetFromJsonAsync<TargetsResult>("/api/v1/admin/backup/targets", CT);
        var local = targets!.Items.Single(t => t.Key == HomesteadApiFactory.BackupTargetKey);

        Assert.Null(local.Connection);
        Assert.True(local.IsAvailable);
    }

    // --- Connect --------------------------------------------------------------------------------------

    [Fact]
    public async Task ConnectThenPoll_StoresAnEncryptedGrantAndFlipsTheCardToAvailable()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        var result = await ConnectAsync(client);

        Assert.Equal(FakeGoogleOAuthClient.UserCode, result.UserCode);
        Assert.Equal("Connected", result.Poll!.Status);
        Assert.Equal("owner@example.test", result.Poll.ConnectedAccountEmail);

        var row = await WithDbAsync(db => db.BackupTargetStates
            .AsNoTracking()
            .SingleAsync(s => s.TargetKey == TargetKey, CT));

        Assert.NotNull(row.EncryptedGrant);
        // At rest it is ciphertext, not the token.
        Assert.DoesNotContain("refresh-token-1", row.EncryptedGrant);
        // …while the email and issue time are plain, so the card renders without decrypting.
        Assert.Equal("owner@example.test", row.ConnectedAccountEmail);
        Assert.NotNull(row.GrantIssuedAt);

        var target = await GetTargetAsync(client);
        Assert.True(target.IsAvailable);
        Assert.True(target.Connection!.IsConnected);
    }

    [Fact]
    public async Task Poll_BeforeTheAdminConsents_ReportsPending()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        Oauth.NextPollResult = new GoogleDevicePollResult(GoogleDevicePollStatus.Pending);

        var start = await client.PostAsync($"/api/v1/admin/backup/targets/{TargetKey}/connect", null, CT);
        var connect = (await start.Content.ReadFromJsonAsync<ConnectResult>(CT))!;

        var poll = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connect/poll",
            new { pollToken = connect.PollToken }, CT);

        var body = (await poll.Content.ReadFromJsonAsync<PollResult>(CT))!;
        Assert.Equal("Pending", body.Status);

        Assert.False(await WithDbAsync(db => db.BackupTargetStates.AnyAsync(
            s => s.TargetKey == TargetKey && s.EncryptedGrant != null, CT)));
    }

    [Fact]
    public async Task Connect_OnALocalTargetIsRefused()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/admin/backup/targets/{HomesteadApiFactory.BackupTargetKey}/connect", null, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The message is built for a human to act on ("check ClientId", "publish your consent screen"), so
    // it has to reach them. Left unmapped it falls through to an unhandled 500 and the dialog shows a
    // generic "An unexpected server error occurred" — the one sentence that helps nobody.
    [Fact]
    public async Task Connect_WhenGoogleRejectsTheClient_Is400WithTheActionableReason()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        Oauth.StartFailure = new GoogleDriveAuthException(
            "invalid_client", "Google rejected the OAuth client id — check Backup:Targets:{key}:ClientId.");

        var response = await client.PostAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connect", null, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("check Backup:Targets", await response.Content.ReadAsStringAsync(CT));
    }

    [Fact]
    public async Task Connect_OnAnUndefinedTargetIs404()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            "/api/v1/admin/backup/targets/nope/connect", null, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Account switching ----------------------------------------------------------------------------

    [Fact]
    public async Task ADifferentAccount_NeedsConfirmationAndResetsTheMirrorBookkeeping()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        await ConnectAsync(client, "first@example.test");
        var sourceId = await CreatePairingAsync(client);

        // Stand in for a mirror that has already run: copies recorded, a folder mapped.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.DocumentBackupStates.Add(new DocumentBackupState
            {
                Id = Guid.NewGuid(),
                BackupSourceId = sourceId,
                DocumentVersionId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                RemoteRelativePath = "Documents/2025/Doc.pdf",
                RemoteHandle = "drive-file-id",
                BackedUpHash = "hash",
                FileSize = 10,
                BackedUpAt = DateTimeOffset.UtcNow,
            });

            db.BackupTargetFolders.Add(new BackupTargetFolder
            {
                Id = Guid.NewGuid(),
                TargetKey = TargetKey,
                Path = "Documents/2025",
                RemoteFolderId = "folder-id",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(CT);
        }

        // A second consent, for a different account.
        Oauth.NextPollResult = FakeGoogleOAuthClient.Granted("refresh-token-2");
        Accounts.NextAccount = new GoogleDriveAccountInfo("second@example.test", null, null);

        var start = await client.PostAsync($"/api/v1/admin/backup/targets/{TargetKey}/connect", null, CT);
        var connect = (await start.Content.ReadFromJsonAsync<ConnectResult>(CT))!;

        var poll = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connect/poll",
            new { pollToken = connect.PollToken }, CT);

        var mismatch = (await poll.Content.ReadFromJsonAsync<PollResult>(CT))!;

        Assert.Equal("AccountMismatch", mismatch.Status);
        Assert.Equal("second@example.test", mismatch.ConnectedAccountEmail);
        Assert.Equal("first@example.test", mismatch.ExistingAccountEmail);
        Assert.NotNull(mismatch.PendingToken);

        // Nothing changed until the admin says so.
        Assert.Equal(
            "first@example.test",
            (await GetTargetAsync(client)).Connection!.ConnectedAccountEmail);
        Assert.True(await WithDbAsync(db => db.DocumentBackupStates.AnyAsync(CT)));

        var confirm = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connect/confirm",
            new { pendingToken = mismatch.PendingToken }, CT);
        confirm.EnsureSuccessStatusCode();

        var confirmed = (await confirm.Content.ReadFromJsonAsync<ConfirmResult>(CT))!;
        Assert.Equal("second@example.test", confirmed.ConnectedAccountEmail);
        Assert.Equal(1, confirmed.ResetCopyCount);

        // Stored file ids mean nothing in the new account's Drive, so every one of them is forgotten and
        // the next run re-uploads in full.
        Assert.False(await WithDbAsync(db => db.DocumentBackupStates.AnyAsync(CT)));
        Assert.False(await WithDbAsync(db => db.BackupTargetFolders.AnyAsync(CT)));

        Assert.Equal(
            "second@example.test",
            (await GetTargetAsync(client)).Connection!.ConnectedAccountEmail);
    }

    // --- Disconnect -----------------------------------------------------------------------------------

    [Fact]
    public async Task Disconnect_RevokesWithGoogleAndClearsTheCard()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        await ConnectAsync(client);

        var response = await client.DeleteAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connection", CT);
        response.EnsureSuccessStatusCode();

        // Google does not invalidate old refresh tokens when issuing new ones, so skipping this would
        // strand a live credential in the user's Google security page forever.
        Assert.Contains("refresh-token-1", Oauth.RevokedTokens);

        var target = await GetTargetAsync(client);
        Assert.False(target.Connection!.IsConnected);
        Assert.Null(target.Connection.ConnectedAccountEmail);
        Assert.False(target.IsAvailable);
    }

    [Fact]
    public async Task Disconnect_WithNothingConnectedIs404()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();

        var response = await client.DeleteAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/connection", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Validate -------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_ReportsFreeSpaceForAWorkingConnection()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        await ConnectAsync(client);

        var response = await client.PostAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/validate", null, CT);
        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadFromJsonAsync<ValidateResult>(CT))!;

        Assert.True(body.IsAvailable);
        Assert.Equal("owner@example.test", body.ConnectedAccountEmail);
        Assert.Equal(1_000, body.StorageUsedBytes);
        Assert.Equal(15_000_000_000, body.StorageLimitBytes);
    }

    // Lazy detection, persisted: from here the card reads unavailable-with-reason and every entry point
    // refuses without touching Google again.
    [Fact]
    public async Task Validate_ADeadGrant_LatchesAndTheLatchSurvivesOnTheCard()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        await ConnectAsync(client);

        Accounts.Failure = new GoogleDriveAuthException("unauthorized", "Google rejected the token.");

        var response = await client.PostAsync(
            $"/api/v1/admin/backup/targets/{TargetKey}/validate", null, CT);
        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadFromJsonAsync<ValidateResult>(CT))!;
        Assert.False(body.IsAvailable);

        var target = await GetTargetAsync(client);
        Assert.False(target.IsAvailable);
        Assert.NotNull(target.Connection!.AuthFailedAt);
        Assert.Contains("rejected", target.Connection.AuthFailedReason);
    }

    [Fact]
    public async Task Validate_AfterReconnecting_ClearsTheLatch()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        await ConnectAsync(client);

        Accounts.Failure = new GoogleDriveAuthException("unauthorized", "Google rejected the token.");
        await client.PostAsync($"/api/v1/admin/backup/targets/{TargetKey}/validate", null, CT);

        Accounts.Failure = null;
        await ConnectAsync(client);

        var target = await GetTargetAsync(client);
        Assert.True(target.IsAvailable);
        Assert.Null(target.Connection!.AuthFailedAt);
    }

    // --- The gate, at the entry points ----------------------------------------------------------------

    [Fact]
    public async Task RunNow_OnAnUnconnectedTarget_RefusesWithTheConnectReason()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        var sourceId = await CreatePairingAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/runs", new { }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Not connected to Google", await response.Content.ReadAsStringAsync(CT));
    }

    [Fact]
    public async Task Scheduling_AnUnconnectedTarget_IsRefused()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        var sourceId = await CreatePairingAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/backup/sources/{sourceId}/schedule",
            new { cronExpression = "0 3 * * *", isEnabled = true, priority = 0 }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The pairing's own row carries the reason too, so the card explains itself without a second call.
    [Fact]
    public async Task APairingOnAnUnconnectedTarget_RendersUnavailableWithTheSameReason()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        var sourceId = await CreatePairingAsync(client);

        var source = (await GetTargetAsync(client)).Sources.Single(s => s.Id == sourceId);

        Assert.False(source.IsAvailable);
        Assert.Contains("Not connected to Google", source.UnavailableReason);
    }

    // --- Continuation atomics -------------------------------------------------------------------------
    //
    // Here rather than in the unit suite because the bump is ExecuteUpdate's database-side increment,
    // which is what keeps the count correct under concurrency and which InMemory does not implement.

    [Fact]
    public async Task ABatchThatRunsOutOfBudget_BumpsItsParentAndEnqueuesAContinuation()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        var sourceId = await CreatePairingAsync(client);

        Guid parentId;
        Guid batchId;

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
                CreatedAt = DateTimeOffset.UtcNow,
                TotalChildCount = 1,
                Priority = -100,
            };

            var batch = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupBatch,
                Status = ProcessingJobStatus.InProgress,
                EntityType = ProcessingEntityType.BackupSource,
                EntityId = sourceId,
                ParentJobId = parent.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                // Long spent: the handler must defer rather than start work.
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                Priority = -101,
            };

            batch.SetPayload(new BackupBatchPayload(sourceId, [
                new BackupAction
                {
                    Kind = BackupActionKind.Delete,
                    VersionId = Guid.NewGuid(),
                    RelativePath = "Documents/2025/Doc.pdf",
                    Handle = "drive-file-id",
                },
            ]));

            db.ProcessingJobs.AddRange(parent, batch);
            await db.SaveChangesAsync(CT);

            parentId = parent.Id;
            batchId = batch.Id;
        }

        // Connect, so the batch's own gate check passes and it reaches the deadline logic.
        await ConnectAsync(client);

        using (var scope = CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<IEnumerable<IJobHandler>>()
                .Single(h => h.Type == ProcessingJobType.BackupBatch);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == batchId, CT);

            // Completes cleanly: running out of budget is pacing, not failure.
            await handler.HandleAsync(job, CT);
        }

        var reloadedParent = await WithDbAsync(db => db.ProcessingJobs
            .AsNoTracking()
            .SingleAsync(j => j.Id == parentId, CT));

        // Bumped in the same transaction as the insert, so parent-progress can never see an uncounted
        // child and terminate the run early.
        Assert.Equal(2, reloadedParent.TotalChildCount);

        var continuation = await WithDbAsync(db => db.ProcessingJobs
            .AsNoTracking()
            .SingleAsync(j => j.ParentJobId == parentId && j.Id != batchId, CT));

        // Equal priority keeps it directly behind its predecessor, and ahead of the final prune batch.
        Assert.Equal(-101, continuation.Priority);
        Assert.Equal(sourceId, continuation.EntityId);

        // Deliberately no assertion on Status: the real job processor is running in this host, and the
        // Signal() that makes a continuation prompt is exactly what lets it claim the row before we look.
        var deferred = continuation.GetPayload<BackupBatchPayload>()!;
        Assert.Equal("Documents/2025/Doc.pdf", Assert.Single(deferred.Actions).RelativePath);
    }

    [Fact]
    public async Task ABatchWhoseParentIsAlreadyCancelled_EnqueuesNoOrphan()
    {
        await ResetAsync();
        var client = await CreateAdminClientAsync();
        var sourceId = await CreatePairingAsync(client);
        await ConnectAsync(client);

        Guid parentId;
        Guid batchId;

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parent = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupToTarget,
                Status = ProcessingJobStatus.Cancelled,
                EntityType = ProcessingEntityType.BackupSource,
                EntityId = sourceId,
                CreatedAt = DateTimeOffset.UtcNow,
                TotalChildCount = 1,
            };

            var batch = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.BackupBatch,
                Status = ProcessingJobStatus.InProgress,
                EntityType = ProcessingEntityType.BackupSource,
                EntityId = sourceId,
                ParentJobId = parent.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            };

            batch.SetPayload(new BackupBatchPayload(sourceId, [
                new BackupAction
                {
                    Kind = BackupActionKind.Delete,
                    VersionId = Guid.NewGuid(),
                    RelativePath = "Documents/2025/Doc.pdf",
                    Handle = "drive-file-id",
                },
            ]));

            db.ProcessingJobs.AddRange(parent, batch);
            await db.SaveChangesAsync(CT);

            parentId = parent.Id;
            batchId = batch.Id;
        }

        using (var scope = CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<IEnumerable<IJobHandler>>()
                .Single(h => h.Type == ProcessingJobType.BackupBatch);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == batchId, CT);

            await handler.HandleAsync(job, CT);
        }

        // The conditional bump matched zero rows, so nothing was inserted.
        Assert.False(await WithDbAsync(db => db.ProcessingJobs
            .AnyAsync(j => j.ParentJobId == parentId && j.Id != batchId, CT)));

        Assert.Equal(1, (await WithDbAsync(db => db.ProcessingJobs
            .AsNoTracking()
            .SingleAsync(j => j.Id == parentId, CT))).TotalChildCount);
    }

    // --- Response shapes ------------------------------------------------------------------------------

    private record ConnectResult(
        string UserCode,
        string VerificationUrl,
        DateTimeOffset ExpiresAt,
        int IntervalSeconds,
        string PollToken)
    {
        public PollResult? Poll { get; init; }
    }

    private record PollResult(
        string Status,
        string? ConnectedAccountEmail,
        string? ExistingAccountEmail,
        string? PendingToken);

    private record ConfirmResult(string TargetKey, string? ConnectedAccountEmail, int ResetCopyCount);

    private record ValidateResult(
        bool IsAvailable,
        string? Reason,
        string? ConnectedAccountEmail,
        long? StorageUsedBytes,
        long? StorageLimitBytes);

    private record ConnectionResult(
        bool IsConnected,
        string? ConnectedAccountEmail,
        DateTimeOffset? GrantIssuedAt,
        DateTimeOffset? AuthFailedAt,
        string? AuthFailedReason);

    private record SourceItem(Guid Id, string SourceKind, bool IsAvailable, string? UnavailableReason);

    private record TargetItem(
        string Key,
        string? Kind,
        string? Location,
        bool IsAvailable,
        string? ConfigurationError,
        bool IsEnabled,
        ConnectionResult? Connection,
        List<SourceItem> Sources);

    private record TargetsResult(List<TargetItem> Items);

    private record SourceResult(Guid Id);
}
