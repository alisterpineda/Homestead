using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Transactions;

/// <summary>
/// The acceptance test for the whole change: while a command's slow AI call is in flight, no pooled
/// connection is left "idle in transaction". Summarize stays synchronous and does its LLM call before the
/// only flush, so lazy-tx removes its connection-hold entirely. Under the old eager-open mechanism this
/// count would be ≥ 1 during the blocked window.
/// </summary>
public class IdleInTransactionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task SummarizeDocument_HoldsNoConnectionIdleInTransaction_WhileAiCallIsBlocked()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "idle-tx-test.pdf");
        var providerId = await SeedEnabledProviderAsync();

        var (entered, release) = FakeAiClassificationService.ArmGate();
        try
        {
            // Fire the summarize without awaiting; it will block inside the fake AI call, which happens
            // BEFORE the handler's only flush — so the command transaction has not opened yet.
            var summarizeTask = client.PostAsJsonAsync(
                $"/api/v1/documents/{docId}/summarize", new { aiProviderId = providerId }, CT);

            await entered.WaitAsync(TimeSpan.FromSeconds(15), CT);

            // During the blocked window: no OTHER backend on this database may be idle in transaction.
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync(CT);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database()
                  AND pid <> pg_backend_pid()
                  AND state = 'idle in transaction'
                """, conn);
            var idleInTx = (long)(await cmd.ExecuteScalarAsync(CT))!;
            Assert.Equal(0, idleInTx);

            // Release the AI call and let the command finish.
            release();
            var response = await summarizeTask;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<SummarizeResult>(CT);
            Assert.Equal(FakeAiClassificationService.GeneratedSummary, result!.Summary);
        }
        finally
        {
            // Defensive: ensure the gate never lingers for the next test even if an assertion threw.
            release();
            FakeAiClassificationService.ResetGate();
        }

        // The summary persisted and an Updated audit row exists.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == docId, CT);
        Assert.Equal(FakeAiClassificationService.GeneratedSummary, doc.Summary);
        Assert.True(await db.AuditLogs.AsNoTracking().AnyAsync(
            a => a.EntityType == "Document" && a.EntityId == docId && a.Action == AuditAction.Updated, CT));
    }

    private async Task<Guid> SeedEnabledProviderAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = $"Test Provider {Guid.NewGuid():N}",
            ProviderType = AiProviderType.AzureAIFoundry,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AiProviders.Add(provider);
        await db.SaveChangesAsync(CT);
        return provider.Id;
    }
}
