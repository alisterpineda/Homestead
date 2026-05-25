using Homestead.WebApi.Domain.Settings;

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
    public Guid? AiProviderId { get; set; }

    public Document Document { get; set; } = null!;
    public AiProvider? AiProvider { get; set; }
}
