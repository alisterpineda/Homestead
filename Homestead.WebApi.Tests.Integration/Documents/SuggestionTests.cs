using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class SuggestionTests : IntegrationTestBase
{
    public SuggestionTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedDocumentWithSuggestionsAsync()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "suggestion-test.pdf");

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

        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "AI Suggested Title",
            OriginalValue = "suggestion-test.pdf",
            AiProviderId = provider.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Field = DocumentSuggestionField.Summary,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "This is an AI-generated summary.",
            AiProviderId = provider.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var document = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        document.Name = "AI Suggested Title";
        document.Summary = "This is an AI-generated summary.";

        await db.SaveChangesAsync(CT);
        return docId;
    }

    [Fact]
    public async Task GetDocument_IncludesPendingSuggestions()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/v1/documents/{docId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDetailItem>(CT);
        Assert.NotNull(doc!.Suggestions);
        Assert.Equal(2, doc.Suggestions.Count);
        Assert.Contains(doc.Suggestions, s => s.Field == "Name");
        Assert.Contains(doc.Suggestions, s => s.Field == "Summary");
    }

    [Fact]
    public async Task ApproveSuggestion_SetsStatusToApproved()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.DocumentSuggestions
            .FirstAsync(s => s.DocumentId == docId && s.Field == DocumentSuggestionField.Name, CT);

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestion.Id}/approve", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        db.ChangeTracker.Clear();
        var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestion.Id, CT);
        Assert.Equal(DocumentSuggestionStatus.Approved, updated.Status);
        Assert.NotNull(updated.ResolvedAt);
    }

    [Fact]
    public async Task DeclineSuggestion_Name_RevertsDocumentName()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.DocumentSuggestions
            .FirstAsync(s => s.DocumentId == docId && s.Field == DocumentSuggestionField.Name, CT);

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestion.Id}/decline", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        db.ChangeTracker.Clear();
        var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestion.Id, CT);
        Assert.Equal(DocumentSuggestionStatus.Declined, updated.Status);

        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal("suggestion-test.pdf", doc.Name);
    }

    [Fact]
    public async Task DeclineSuggestion_Summary_ClearsSummary()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.DocumentSuggestions
            .FirstAsync(s => s.DocumentId == docId && s.Field == DocumentSuggestionField.Summary, CT);

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestion.Id}/decline", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        db.ChangeTracker.Clear();
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Null(doc.Summary);
    }

    [Fact]
    public async Task ApproveAllSuggestions_ApprovesAllPending()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/approve-all", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.DocumentSuggestions
            .Where(s => s.DocumentId == docId && s.Status == DocumentSuggestionStatus.Pending)
            .CountAsync(CT);
        Assert.Equal(0, pending);

        var approved = await db.DocumentSuggestions
            .Where(s => s.DocumentId == docId && s.Status == DocumentSuggestionStatus.Approved)
            .CountAsync(CT);
        Assert.Equal(2, approved);
    }

    [Fact]
    public async Task PatchDocument_Summary_AutoDeclinesSummarySuggestion()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        var patchOps = new[]
        {
            new { op = "replace", path = "/summary", value = "User-written summary" }
        };

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", patchOps);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var summarySuggestion = await db.DocumentSuggestions
            .FirstAsync(s => s.DocumentId == docId && s.Field == DocumentSuggestionField.Summary, CT);
        Assert.Equal(DocumentSuggestionStatus.Declined, summarySuggestion.Status);
        Assert.NotNull(summarySuggestion.ResolvedAt);

        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal("User-written summary", doc.Summary);
    }

    [Fact]
    public async Task DeclineAllSuggestions_DeclinesAllAndRevertsChanges()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/decline-all", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal("suggestion-test.pdf", doc.Name);
        Assert.Null(doc.Summary);

        var declined = await db.DocumentSuggestions
            .Where(s => s.DocumentId == docId && s.Status == DocumentSuggestionStatus.Declined)
            .CountAsync(CT);
        Assert.Equal(2, declined);
    }
}
