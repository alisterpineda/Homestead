namespace Homestead.WebApi.Modules.Documents.Services;

public record ClassificationResult(
    List<Guid> MatchedTagIds,
    List<Guid> MatchedPartyIds,
    string Summary,
    string SuggestedTitle);
