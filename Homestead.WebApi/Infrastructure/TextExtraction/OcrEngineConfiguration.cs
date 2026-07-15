using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Reads the "Ocr:Engines" section into named engine instances, and validates each per its Type.
//
// Validation returns an error string rather than throwing: there is deliberately no ValidateOnStart here
// (mirroring AiOptions' no-startup-validator posture). A misconfigured engine must not stop the app from
// booting — the build-time OpenAPI generator boots the host with no meaningful config, and an operator
// with a typo in an Azure endpoint should still get a running app. The cost is that config errors surface
// at first extraction; the mitigation is that ConfigurationError is exposed on the picker, so an engine
// that cannot run reads as "unavailable, because <reason>" before anyone selects it.
public static class OcrEngineConfiguration
{
    public static IReadOnlyList<(string Key, OcrEngineOptions Options)> ReadEngines(IConfiguration configuration)
    {
        var engines = new List<(string Key, OcrEngineOptions Options)>();

        foreach (var child in configuration.GetSection(OcrEngineOptions.SectionName).GetChildren())
            engines.Add((child.Key, child.Get<OcrEngineOptions>() ?? new OcrEngineOptions()));

        // The built-in Tesseract engine is always present. Two jobs: it makes the zero-config case work
        // (no "Ocr:Engines" at all -> today's defaults, so existing deployments and tests are untouched),
        // and it guarantees the resolution chain's terminus actually exists, so a dangling household
        // default always has something real to fall back to.
        if (!engines.Any(e => string.Equals(e.Key, OcrEngineOptions.BuiltInTesseractKey, StringComparison.OrdinalIgnoreCase)))
        {
            engines.Insert(0, (OcrEngineOptions.BuiltInTesseractKey,
                new OcrEngineOptions { Type = TextExtractionEngineType.Tesseract }));
        }

        return engines;
    }

    // Returns null when the instance is usable, else a human-readable reason shown in the picker.
    public static string? Validate(string key, OcrEngineOptions options) => options.ResolveType(key) switch
    {
        TextExtractionEngineType.Tesseract => ValidateTesseract(options),
        TextExtractionEngineType.AzureDocumentIntelligence => ValidateAzure(options),
        // Only reachable for a non-built-in key with no Type: refuse rather than defaulting to Tesseract,
        // which would silently run the wrong engine under the operator's chosen name.
        null => $"Type is required (one of: {string.Join(", ", Enum.GetNames<TextExtractionEngineType>())}).",
        var type => $"Unknown engine type '{type}'."
    };

    private static string? ValidateTesseract(OcrEngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TessDataPath))
            return "TessDataPath is required.";

        // No language check: an empty list is legal and means English (see OcrEngineOptions.Languages).
        return null;
    }

    private static string? ValidateAzure(OcrEngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            return "Endpoint is required.";
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
            return "Endpoint must be an absolute URI.";
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return "ApiKey is required.";
        if (string.IsNullOrWhiteSpace(options.ModelId))
            return "ModelId is required.";

        // Caught here rather than at the API call: Azure rejects the combination, and a 400 from a
        // background job is a far worse way to learn about it than the picker saying so up front.
        if (options.OutputContentFormat == AzureOutputContentFormat.Markdown
            && !AzureModels.SupportsMarkdown(options.ModelId))
        {
            return $"OutputContentFormat 'Markdown' requires a layout model; '{options.ModelId}' does not support it.";
        }

        return null;
    }
}
