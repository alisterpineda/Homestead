using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
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

        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "AI Suggested Title",
            OriginalValue = "suggestion-test.pdf",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var document = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        document.Name = "AI Suggested Title";
        document.Summary = "This is an AI-generated summary.";

        await db.SaveChangesAsync(CT);
        return docId;
    }

    // Seeds a document whose date field holds an AI-applied value, with the pending suggestion recording the
    // original (null = the date was empty) — the state DocumentClassifier.ApplyDate leaves behind.
    private async Task<(Guid DocId, Guid SuggestionId)> SeedDocumentWithDateSuggestionAsync(
        DocumentSuggestionField field, DateOnly suggested, DateOnly? original)
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "date-suggestion-test.pdf");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suggestion = new DocumentSuggestion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Field = field,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = suggested.ToString("yyyy-MM-dd"),
            OriginalValue = original?.ToString("yyyy-MM-dd"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DocumentSuggestions.Add(suggestion);

        var document = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        if (field == DocumentSuggestionField.DocumentDate)
            document.DocumentDate = suggested;
        else
            document.ExpiryDate = suggested;

        await db.SaveChangesAsync(CT);
        return (docId, suggestion.Id);
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
        Assert.Equal(1, doc.Suggestions.Count);
        Assert.Contains(doc.Suggestions, s => s.Field == "Name");
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
    public async Task DeclineSuggestion_DocumentDate_RestoresOriginal()
    {
        var (docId, suggestionId) = await SeedDocumentWithDateSuggestionAsync(
            DocumentSuggestionField.DocumentDate, new DateOnly(2025, 3, 15), new DateOnly(2025, 1, 1));
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestionId}/decline", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestionId, CT);
        Assert.Equal(DocumentSuggestionStatus.Declined, updated.Status);
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal(new DateOnly(2025, 1, 1), doc.DocumentDate);
    }

    [Fact]
    public async Task DeclineSuggestion_ExpiryDate_EmptyOriginal_ClearsDate()
    {
        var (docId, suggestionId) = await SeedDocumentWithDateSuggestionAsync(
            DocumentSuggestionField.ExpiryDate, new DateOnly(2030, 6, 30), original: null);
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestionId}/decline", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Null(doc.ExpiryDate);
    }

    [Fact]
    public async Task ApproveSuggestion_DocumentDate_KeepsDate()
    {
        var (docId, suggestionId) = await SeedDocumentWithDateSuggestionAsync(
            DocumentSuggestionField.DocumentDate, new DateOnly(2025, 3, 15), new DateOnly(2025, 1, 1));
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestionId}/approve", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestionId, CT);
        Assert.Equal(DocumentSuggestionStatus.Approved, updated.Status);
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal(new DateOnly(2025, 3, 15), doc.DocumentDate);
    }

    [Fact]
    public async Task PatchDocument_DocumentDate_AutoDeclinesPendingDateSuggestion()
    {
        var (docId, suggestionId) = await SeedDocumentWithDateSuggestionAsync(
            DocumentSuggestionField.DocumentDate, new DateOnly(2025, 3, 15), new DateOnly(2025, 1, 1));
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/documentDate", value = (object)"2025-02-02" }
        });

        response.EnsureSuccessStatusCode();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestionId, CT);
        Assert.Equal(DocumentSuggestionStatus.Declined, updated.Status);
        Assert.NotNull(updated.ResolvedAt);
        // The manual edit wins: an auto-decline doesn't revert to the original.
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal(new DateOnly(2025, 2, 2), doc.DocumentDate);
    }

    // Every AI-suggestible field's patch path auto-declines that field's pending suggestion, and only that one.
    [Theory]
    [InlineData("/tagPaths", DocumentSuggestionField.Tags)]
    [InlineData("/partyIds", DocumentSuggestionField.Parties)]
    [InlineData("/name", DocumentSuggestionField.Name)]
    [InlineData("/documentDate", DocumentSuggestionField.DocumentDate)]
    [InlineData("/expiryDate", DocumentSuggestionField.ExpiryDate)]
    public async Task PatchDocument_ManualEdit_AutoDeclinesOnlyThatFieldsSuggestion(
        string path, DocumentSuggestionField field)
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "patch-auto-decline.pdf");
        var otherField = field == DocumentSuggestionField.Name
            ? DocumentSuggestionField.DocumentDate
            : DocumentSuggestionField.Name;

        Guid editedId, otherId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            DocumentSuggestion Pending(DocumentSuggestionField f) => new()
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Field = f,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = f is DocumentSuggestionField.Tags or DocumentSuggestionField.Parties
                    ? Guid.NewGuid().ToString()
                    : "2025-03-15",
                CreatedAt = DateTimeOffset.UtcNow
            };
            var edited = Pending(field);
            var other = Pending(otherField);
            db.DocumentSuggestions.AddRange(edited, other);
            await db.SaveChangesAsync(CT);
            editedId = edited.Id;
            otherId = other.Id;
        }

        object value = field switch
        {
            DocumentSuggestionField.Tags => Array.Empty<string>(),
            DocumentSuggestionField.Parties => Array.Empty<Guid>(),
            DocumentSuggestionField.Name => "Manually named",
            _ => "2025-02-02"
        };
        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path, value }
        });

        response.EnsureSuccessStatusCode();

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var edited = await db.DocumentSuggestions.FirstAsync(s => s.Id == editedId, CT);
            Assert.Equal(DocumentSuggestionStatus.Declined, edited.Status);
            Assert.NotNull(edited.ResolvedAt);
            var other = await db.DocumentSuggestions.FirstAsync(s => s.Id == otherId, CT);
            Assert.Equal(DocumentSuggestionStatus.Pending, other.Status);
        }
    }

    [Fact]
    public async Task PatchDocument_Notes_LeavesPendingDateSuggestion()
    {
        var (docId, suggestionId) = await SeedDocumentWithDateSuggestionAsync(
            DocumentSuggestionField.DocumentDate, new DateOnly(2025, 3, 15), new DateOnly(2025, 1, 1));
        var client = await CreateAdminClientAsync();

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/notes", value = (object)"edited" }
        });

        response.EnsureSuccessStatusCode();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestionId, CT);
        Assert.Equal(DocumentSuggestionStatus.Pending, updated.Status);
    }

    [Fact]
    public async Task DeclineSuggestion_Parties_UnlinksParty()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "party-suggestion-test.pdf");

        Guid suggestionId;
        Guid partyId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var party = new Party
            {
                Id = Guid.NewGuid(),
                Name = $"ACME Insurance {Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Parties.Add(party);
            partyId = party.Id;

            db.DocumentParties.Add(new DocumentParty { DocumentId = docId, PartyId = partyId });

            var suggestion = new DocumentSuggestion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Field = DocumentSuggestionField.Parties,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = partyId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.DocumentSuggestions.Add(suggestion);
            suggestionId = suggestion.Id;

            await db.SaveChangesAsync(CT);
        }

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/{suggestionId}/decline", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updated = await db.DocumentSuggestions.FirstAsync(s => s.Id == suggestionId, CT);
            Assert.Equal(DocumentSuggestionStatus.Declined, updated.Status);

            var stillLinked = await db.DocumentParties
                .AnyAsync(dp => dp.DocumentId == docId && dp.PartyId == partyId, CT);
            Assert.False(stillLinked);
        }
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
        Assert.Equal(1, approved);
    }

    [Fact]
    public async Task DeclineAllSuggestions_DeclinesAllAndRevertsChanges()
    {
        var docId = await SeedDocumentWithSuggestionsAsync();
        var client = await CreateAdminClientAsync();

        // Plus an AI-applied expiry date over an empty original.
        using (var seedScope = CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.DocumentSuggestions.Add(new DocumentSuggestion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Field = DocumentSuggestionField.ExpiryDate,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = "2030-06-30",
                OriginalValue = null,
                CreatedAt = DateTimeOffset.UtcNow
            });
            var seeded = await seedDb.Documents.FirstAsync(d => d.Id == docId, CT);
            seeded.ExpiryDate = new DateOnly(2030, 6, 30);
            await seedDb.SaveChangesAsync(CT);
        }

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/decline-all", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        Assert.Equal("suggestion-test.pdf", doc.Name);
        Assert.Equal("This is an AI-generated summary.", doc.Summary);
        Assert.Null(doc.ExpiryDate);

        var declined = await db.DocumentSuggestions
            .Where(s => s.DocumentId == docId && s.Status == DocumentSuggestionStatus.Declined)
            .CountAsync(CT);
        Assert.Equal(2, declined);
    }

    [Fact]
    public async Task DeclineAllSuggestions_Parties_UnlinksParty()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "decline-all-party-test.pdf");

        Guid partyId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var party = new Party
            {
                Id = Guid.NewGuid(),
                Name = $"ACME Insurance {Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Parties.Add(party);
            partyId = party.Id;

            db.DocumentParties.Add(new DocumentParty { DocumentId = docId, PartyId = partyId });
            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                Field = DocumentSuggestionField.Parties,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = partyId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(CT);
        }

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/suggestions/decline-all", null, CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stillLinked = await db.DocumentParties
                .AnyAsync(dp => dp.DocumentId == docId && dp.PartyId == partyId, CT);
            Assert.False(stillLinked);

            var declined = await db.DocumentSuggestions
                .AnyAsync(s => s.DocumentId == docId
                    && s.Field == DocumentSuggestionField.Parties
                    && s.Status == DocumentSuggestionStatus.Declined, CT);
            Assert.True(declined);
        }
    }
}
