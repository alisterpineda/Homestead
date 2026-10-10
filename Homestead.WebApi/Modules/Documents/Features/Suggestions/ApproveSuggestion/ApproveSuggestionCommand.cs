namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;

public record ApproveSuggestionCommand
{
    public Guid DocumentId { get; init; }
    public Guid SuggestionId { get; init; }
}
