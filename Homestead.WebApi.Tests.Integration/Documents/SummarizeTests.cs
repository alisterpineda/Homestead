using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class SummarizeTests : IntegrationTestBase
{
    public SummarizeTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Summarize_GeneratesSummary_Persisted_AndAudited()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "summarize-test.pdf");

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/summarize", null, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SummarizeResult>(CT);
        // Asserting a non-null canned summary is the false-green guard: if the fake IAiProvider
        // registration goes missing, the real factory reports unconfigured, summarize no-ops, and this fails.
        Assert.Equal(FakeAiClassificationService.GeneratedSummary, result!.Summary);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == docId, CT);
        Assert.Equal(FakeAiClassificationService.GeneratedSummary, doc.Summary);

        var auditRow = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "Document" && a.EntityId == docId && a.Action == AuditAction.Updated)
            .FirstOrDefaultAsync(CT);
        Assert.NotNull(auditRow);
        Assert.Contains("Summary", auditRow!.Changes);
    }

    [Fact]
    public async Task Summarize_RequiresAuth_Returns401()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "summarize-auth-test.pdf");

        var anonClient = CreateAnonymousClient();
        var response = await anonClient.PostAsync(
            $"/api/v1/documents/{docId}/summarize", null, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
