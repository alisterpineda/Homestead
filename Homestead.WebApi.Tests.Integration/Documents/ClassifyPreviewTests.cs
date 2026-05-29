using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class ClassifyPreviewTests : IntegrationTestBase
{
    public ClassifyPreviewTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ClassifyPreview_Returns200()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "preview-test.pdf");

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/classify-preview", null, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ClassifyPreviewResult>(CT);
        Assert.NotNull(result);
        Assert.NotNull(result!.Tags);
    }

    [Fact]
    public async Task ClassifyPreview_DoesNotCreateSuggestions()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "no-suggestions-test.pdf");

        await client.PostAsync(
            $"/api/v1/documents/{docId}/classify-preview", null, CT);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestions = await db.DocumentSuggestions
            .Where(s => s.DocumentId == docId)
            .ToListAsync(CT);
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task ClassifyPreview_DoesNotMutateDocument()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "immutable-test.pdf");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var docBefore = await db.Documents
            .AsNoTracking()
            .Include(d => d.DocumentTags)
            .FirstAsync(d => d.Id == docId, CT);
        var originalName = docBefore.Name;
        var originalSummary = docBefore.Summary;
        var originalTagCount = docBefore.DocumentTags.Count;

        await client.PostAsync(
            $"/api/v1/documents/{docId}/classify-preview", null, CT);

        db.ChangeTracker.Clear();
        var docAfter = await db.Documents
            .AsNoTracking()
            .Include(d => d.DocumentTags)
            .FirstAsync(d => d.Id == docId, CT);

        Assert.Equal(originalName, docAfter.Name);
        Assert.Equal(originalSummary, docAfter.Summary);
        Assert.Equal(originalTagCount, docAfter.DocumentTags.Count);
    }

    [Fact]
    public async Task ClassifyPreview_ScopedToTags_LeavesPendingNameSuggestionUntouched()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "scoped-preview-test.pdf");

        Guid providerId;
        Guid suggestionId;
        using (var scope = CreateScope())
        {
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
            providerId = provider.Id;

            var suggestion = new DocumentSuggestion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Field = DocumentSuggestionField.Name,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = "AI Suggested Title",
                OriginalValue = "scoped-preview-test.pdf",
                AiProviderId = provider.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.DocumentSuggestions.Add(suggestion);
            await db.SaveChangesAsync(CT);
            suggestionId = suggestion.Id;
        }

        // Pass the provider id so the request resolves a provider and actually drives the (fake)
        // classifier with a Tags-only scope — not a null-provider short-circuit.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/classify-preview",
            new { aiProviderId = providerId, fields = new[] { "Tags" } }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Preview never mutates suggestions: the pending Name suggestion survives untouched and no
        // new suggestions are created, even though the classifier ran under a Tags-only scope.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suggestions = await db.DocumentSuggestions
                .Where(s => s.DocumentId == docId)
                .ToListAsync(CT);
            Assert.Single(suggestions);
            Assert.Equal(suggestionId, suggestions[0].Id);
            Assert.Equal(DocumentSuggestionStatus.Pending, suggestions[0].Status);
            Assert.Equal(DocumentSuggestionField.Name, suggestions[0].Field);
        }
    }

    [Fact]
    public async Task ClassifyPreview_RequiresAuth_Returns401()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "auth-test.pdf");

        var anonClient = CreateAnonymousClient();
        var response = await anonClient.PostAsync(
            $"/api/v1/documents/{docId}/classify-preview", null, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
