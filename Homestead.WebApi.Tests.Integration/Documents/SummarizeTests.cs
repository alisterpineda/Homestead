using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class SummarizeTests : IntegrationTestBase
{
    public SummarizeTests(PostgresFixture fixture) : base(fixture) { }

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

    [Fact]
    public async Task Summarize_GeneratesSummary_Persisted_AndAudited()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "summarize-test.pdf");
        var providerId = await SeedEnabledProviderAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/summarize",
            new { aiProviderId = providerId }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SummarizeResult>(CT);
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
    public async Task Summarize_NoProvider_Returns200_SummaryUnchanged()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "summarize-noprovider-test.pdf");

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/summarize", null, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SummarizeResult>(CT);
        Assert.Null(result!.Summary);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == docId, CT);
        Assert.Null(doc.Summary);
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
