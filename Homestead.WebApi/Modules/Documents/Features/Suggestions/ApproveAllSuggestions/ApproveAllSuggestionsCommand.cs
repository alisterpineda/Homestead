namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;

public record ApproveAllSuggestionsCommand
{
    public Guid DocumentId { get; init; }
}
