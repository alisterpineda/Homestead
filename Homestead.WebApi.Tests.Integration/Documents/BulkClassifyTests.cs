using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class BulkClassifyTests : IntegrationTestBase
{
    public BulkClassifyTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task BulkClassify_AuditsActualDocumentDiff_NotSuggestions()
    {
        // The deterministic fake returns no tag/name/party matches, so a "fresh" classify changes
        // nothing. To exercise the full-diff audit on the classify path we seed a previously
        // AI-applied (but still pending) tag: refreshing Tags reverts it and re-suggests nothing,
        // so the document's TagPaths genuinely change — and that real DB diff is what gets audited,
        // independent of the suggestion rows.
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "bulk-classify-audit.pdf");
        var providerId = await SeedEnabledProviderAsync();

        Guid tagId = Guid.NewGuid();
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tags.Add(new Tag
            {
                Id = tagId,
                Name = "Finance",
                Path = "Finance",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.DocumentTags.Add(new DocumentTag { DocumentId = docId, TagId = tagId });
            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Field = DocumentSuggestionField.Tags,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = tagId.ToString(),
                AiProviderId = providerId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(CT);
        }

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-classify",
            new { ids = new[] { docId }, aiProviderId = providerId, fields = new[] { "Tags" } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkClassifyResult>(CT);
        Assert.Single(body!.JobIds);

        await WaitForJobCompletionAsync(body.JobIds[0]);

        using var verifyScope = CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The previously-applied tag was reverted and not re-suggested → the tag link is gone.
        var tagLinkCount = await verifyDb.DocumentTags.CountAsync(dt => dt.DocumentId == docId, CT);
        Assert.Equal(0, tagLinkCount);

        // That real change is audited as a Document/Updated row whose changes include TagPaths.
        var auditRow = await verifyDb.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "Document" && a.EntityId == docId && a.Action == AuditAction.Updated)
            .FirstOrDefaultAsync(CT);
        Assert.NotNull(auditRow);
        Assert.Contains("TagPaths", auditRow!.Changes);
    }
}
