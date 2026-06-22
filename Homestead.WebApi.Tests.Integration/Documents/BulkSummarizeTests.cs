using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class BulkSummarizeTests : IntegrationTestBase
{
    public BulkSummarizeTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task BulkSummarize_SetsSummary_AndCreatesNoSuggestions()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "bulk-summarize-1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "bulk-summarize-2.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-summarize",
            new { ids = new[] { docId1, docId2 } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkClassifyResult>(CT);
        Assert.Single(body!.JobIds);

        await WaitForJobCompletionAsync(body.JobIds[0]);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var docId in new[] { docId1, docId2 })
        {
            var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == docId, CT);
            Assert.Equal(FakeAiClassificationService.GeneratedSummary, doc.Summary);

            // The bulk (background-job) summarize path now writes a Document/Updated audit row,
            // matching the single-doc summarize path (see SummarizeTests).
            var auditRow = await db.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityType == "Document" && a.EntityId == docId && a.Action == AuditAction.Updated)
                .FirstOrDefaultAsync(CT);
            Assert.NotNull(auditRow);
            Assert.Contains("Summary", auditRow!.Changes);
        }

        // Summarize must never create suggestion rows — this is what distinguishes it from classify.
        var suggestionCount = await db.DocumentSuggestions
            .CountAsync(s => s.DocumentId == docId1 || s.DocumentId == docId2, CT);
        Assert.Equal(0, suggestionCount);
    }

    [Fact]
    public async Task BulkSummarize_EmptyIds_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-summarize",
            new { ids = Array.Empty<Guid>() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkSummarize_NonExistentIds_ReturnsAccepted_WithEmptyJobIds()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-summarize",
            new { ids = new[] { Guid.NewGuid() } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkClassifyResult>(CT);
        Assert.Empty(body!.JobIds);
    }

    [Fact]
    public async Task BulkSummarize_AsViewer_Returns403()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "bulk-summarize-forbidden.pdf");

        var viewerClient = await CreateViewerClientAsync();
        var response = await viewerClient.PostAsJsonAsync("/api/v1/documents/bulk-summarize",
            new { ids = new[] { docId } }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
