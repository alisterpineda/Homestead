namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;

public record DeclineAllSuggestionsCommand
{
    public Guid DocumentId { get; init; }
}
