using Homestead.WebApi.Common;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public sealed class TextExtractionEngineResolver(IEnumerable<ITextExtractionEngine> engines, string? defaultEngineKey)
    : ITextExtractionEngineResolver
{
    private readonly IReadOnlyList<ITextExtractionEngine> _engines = engines.ToList();

    // Computed once: the engine set and the configured default are both fixed at startup.
    public string DefaultKey { get; } = OcrEngineConfiguration.ResolveDefaultKey(defaultEngineKey);

    public string ResolveKey(string? requestedKey)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
            return DefaultKey;

        // SingleOrDefault so a duplicate key is a loud throw, never one instance shadowing the other.
        var engine = _engines.SingleOrDefault(e =>
            string.Equals(e.Key, requestedKey, StringComparison.OrdinalIgnoreCase));

        if (engine is null)
            throw new BusinessRuleException($"Text extraction engine '{requestedKey}' is not configured.");

        if (!engine.IsConfigured)
            throw new BusinessRuleException(
                $"Text extraction engine '{requestedKey}' is unavailable: {engine.ConfigurationError}");

        // Canonical casing, so the payload and provenance metadata record the configured key rather than
        // however the caller happened to spell it.
        return engine.Key;
    }
}
