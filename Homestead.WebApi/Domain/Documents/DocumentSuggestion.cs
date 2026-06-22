namespace Homestead.WebApi.Domain.Documents;

public class DocumentSuggestion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public DocumentSuggestionField Field { get; set; }
    public DocumentSuggestionStatus Status { get; set; }
    public required string SuggestedValue { get; set; }
    public string? OriginalValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool IsApplied { get; set; } = true;

    public Document Document { get; set; } = null!;
}
