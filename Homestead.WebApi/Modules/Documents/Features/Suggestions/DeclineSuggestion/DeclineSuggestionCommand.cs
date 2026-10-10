namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;

public record DeclineSuggestionCommand
{
    public Guid DocumentId { get; init; }
    public Guid SuggestionId { get; init; }
}
