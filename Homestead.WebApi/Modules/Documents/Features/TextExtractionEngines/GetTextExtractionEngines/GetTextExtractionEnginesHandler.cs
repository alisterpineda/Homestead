using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;

// Reads straight off the registered engines rather than a hand-kept list, so the picker cannot drift
// from what is actually registered — that drift is exactly what the old DB-backed engine table cost.
public class GetTextExtractionEnginesHandler(
    IEnumerable<ITextExtractionEngine> engines,
    ITextExtractionEngineResolver resolver)
{
    public Task<List<TextExtractionEngineResponse>> Handle(
        GetTextExtractionEnginesQuery request, CancellationToken ct)
    {
        // Matched by key (case-insensitively, like config keys) rather than by resolving: the resolver
        // returns the default VERBATIM even when dangling, and never substitutes.
        var defaultKey = resolver.DefaultKey;

        var result = engines
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select(e => new TextExtractionEngineResponse(
                e.Key,
                DisplayName(e.Type),
                e.Type,
                e.IsConfigured,
                e.ConfigurationError,
                IsDefault: string.Equals(e.Key, defaultKey, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Append a dangling default as its own row (unavailable, marked default) rather than leaving the
        // list with no default marked — the settings page is where an operator debugs exactly this typo.
        // Mirrors GetAiStatusHandler. Extraction itself still runs: TextExtractionHelper falls back to
        // the built-in engine and records the substitution in provenance.
        if (!result.Any(r => r.IsDefault))
        {
            result.Add(new TextExtractionEngineResponse(
                defaultKey,
                "Not defined",
                Type: null,
                IsConfigured: false,
                ConfigurationError: $"Text extraction engine '{defaultKey}' is not defined under {OcrEngineOptions.SectionName}.",
                IsDefault: true));
        }

        return Task.FromResult(result);
    }

    private static string DisplayName(TextExtractionEngineType type) => type switch
    {
        TextExtractionEngineType.Tesseract => "Tesseract",
        TextExtractionEngineType.AzureDocumentIntelligence => "Azure Document Intelligence",
        _ => type.ToString()
    };
}
