using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic stand-in for the real AI service so integration tests can exercise the
/// classify/summarize flows without a live model. Returns only a canned summary (gated by scope)
/// and an unchanged title — no tag/party matches, keeping results FK-safe regardless of the DB.
/// Only reached when a provider resolves (an enabled provider is seeded + its id is passed).
/// </summary>
public sealed class FakeAiClassificationService : IAiClassificationService
{
    public const string GeneratedSummary = "AI generated summary.";

    public Task<ClassificationResult?> ClassifyAsync(
        AiProvider provider,
        string documentName,
        string? extractedText,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct)
    {
        var summary = scope.HasFlag(ClassificationScope.Summary) ? GeneratedSummary : string.Empty;
        return Task.FromResult<ClassificationResult?>(
            new ClassificationResult([], [], summary, documentName));
    }
}
