using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions;

public record DocumentSuggestionDto
{
    public required Guid Id { get; init; }
    public required DocumentSuggestionField Field { get; init; }
    public required DocumentSuggestionStatus Status { get; init; }
    public required string SuggestedValue { get; init; }
    public string? OriginalValue { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}
