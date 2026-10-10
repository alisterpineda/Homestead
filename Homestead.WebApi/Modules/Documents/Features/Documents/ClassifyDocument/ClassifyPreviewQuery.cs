using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyPreviewQuery
{
    public Guid DocumentId { get; init; }

    /// <summary>
    /// The fields to classify. Null or empty defaults to all classify fields (Tags, Parties, Name).
    /// Summary is never part of the classify scope — use the separate summarize action instead.
    /// </summary>
    public List<DocumentSuggestionField>? Fields { get; init; }
}
