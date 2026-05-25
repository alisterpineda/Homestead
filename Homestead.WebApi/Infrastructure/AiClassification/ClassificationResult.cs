namespace Homestead.WebApi.Infrastructure.AiClassification;

public record ClassificationResult(
    List<Guid> MatchedTagIds,
    string Summary,
    string SuggestedTitle);
