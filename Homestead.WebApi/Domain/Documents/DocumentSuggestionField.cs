namespace Homestead.WebApi.Domain.Documents;

/// <summary>
/// The AI-suggestible document fields. Adding one needs no schema change (stored as a string), but it does
/// touch each of these — the first three fail <c>SuggestionFieldCoverageTests</c> when missed:
/// <list type="bullet">
/// <item><c>ClassificationScope</c>: a flag, included in <c>All</c>, mapped in <c>FromFields</c>, and in
/// <c>ShouldAttachImage</c> if it reads layout.</item>
/// <item><c>DocumentSuggestionReverter</c>: how a decline restores the original.</item>
/// <item><c>PatchDocumentHandler.SuggestionFieldsByPatchPath</c>: the patch path whose manual edit
/// auto-declines it.</item>
/// <item><c>AiClassificationService</c>: prompt instruction and rules, response-schema property, reply DTO
/// property and scope gating; <c>ClassificationResult</c> carries the value.</item>
/// <item><c>DocumentClassifier.ApplyAsync</c>: apply it and record the pending suggestion.</item>
/// <item>Frontend: regenerate the API client, the classify dialog checkbox, and the detail-page badge and
/// approve/decline controls.</item>
/// </list>
/// </summary>
public enum DocumentSuggestionField
{
    Tags,
    Name,
    Parties,
    DocumentDate,
    ExpiryDate
}
