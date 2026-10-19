using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class DocumentSuggestionReverterTests
{
    private static DocumentSuggestion Suggestion(DocumentSuggestionField field, string suggested, string? original) =>
        new()
        {
            Field = field,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = suggested,
            OriginalValue = original,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public void Revert_DocumentDate_RestoresOriginal_WithoutSearchVectorUpdate()
    {
        var document = new Document { Name = "Doc", DocumentDate = new DateOnly(2025, 3, 15) };

        var needsSearchUpdate = DocumentSuggestionReverter.Revert(
            document, Suggestion(DocumentSuggestionField.DocumentDate, "2025-03-15", "2025-01-01"));

        Assert.Equal(new DateOnly(2025, 1, 1), document.DocumentDate);
        Assert.False(needsSearchUpdate);
    }

    [Fact]
    public void Revert_DocumentDate_EmptyOriginal_ClearsDate()
    {
        var document = new Document { Name = "Doc", DocumentDate = new DateOnly(2025, 3, 15) };

        DocumentSuggestionReverter.Revert(
            document, Suggestion(DocumentSuggestionField.DocumentDate, "2025-03-15", original: null));

        Assert.Null(document.DocumentDate);
    }

    [Fact]
    public void Revert_ExpiryDate_RestoresOriginal()
    {
        var document = new Document { Name = "Doc", ExpiryDate = new DateOnly(2030, 6, 30) };

        DocumentSuggestionReverter.Revert(
            document, Suggestion(DocumentSuggestionField.ExpiryDate, "2030-06-30", "2029-06-30"));

        Assert.Equal(new DateOnly(2029, 6, 30), document.ExpiryDate);
    }

    [Fact]
    public void Revert_ExpiryDate_EmptyOriginal_ClearsDate_LeavesDocumentDateAlone()
    {
        var document = new Document
        {
            Name = "Doc", DocumentDate = new DateOnly(2025, 1, 1), ExpiryDate = new DateOnly(2030, 6, 30)
        };

        DocumentSuggestionReverter.Revert(
            document, Suggestion(DocumentSuggestionField.ExpiryDate, "2030-06-30", original: null));

        Assert.Null(document.ExpiryDate);
        Assert.Equal(new DateOnly(2025, 1, 1), document.DocumentDate);
    }
}
