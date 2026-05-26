namespace Homestead.WebApi.Modules.Documents.Services;

public record ClassificationResult(
    List<Guid> MatchedTagIds,
    string Summary,
    string SuggestedTitle);
