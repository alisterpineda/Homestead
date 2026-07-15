using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Documents.Features.Versions;

/// <summary>
/// How a version's extracted text was produced. Deliberately FLAT rather than mirroring the persisted
/// <see cref="TextExtractionMetadata"/> union: a polymorphic C# hierarchy generates an awkward
/// discriminated union through ng-openapi-gen, and this is read-only display data. <see cref="Engine"/>
/// carries the type slug, and the type-specific fields are simply null for the engine that did not run.
/// </summary>
public record TextExtractionProvenanceResponse
{
    /// <summary>Engine type slug — "tesseract" or "azure-document-intelligence".</summary>
    public required string Engine { get; init; }

    /// <summary>The configured engine instance that ran, e.g. "azure-layout".</summary>
    public required string EngineKey { get; init; }

    public required DateTimeOffset ExtractedAt { get; init; }

    /// <summary>Set only when the engine asked for was unavailable and a fallback ran instead.</summary>
    public string? RequestedEngineKey { get; init; }

    public string? FallbackReason { get; init; }

    // --- Tesseract ---
    public string? Language { get; init; }
    public int? Dpi { get; init; }
    public int? Psm { get; init; }
    public int? Oem { get; init; }
    public int? PagesFromTextLayer { get; init; }
    public int? PagesOcred { get; init; }

    // --- Azure Document Intelligence ---
    public string? ModelId { get; init; }
    public AzureOutputContentFormat? OutputContentFormat { get; init; }
    public int? PageCount { get; init; }

    /// <summary>
    /// Maps the persisted jsonb. Returns null for a version extracted before provenance existed, and
    /// also for a blob that no longer deserializes — a shape that has moved on is not worth failing a
    /// read for when the payload is display-only.
    /// </summary>
    public static TextExtractionProvenanceResponse? From(string? json)
    {
        return TextExtractionMetadata.Deserialize(json) switch
        {
            TesseractTextExtractionMetadata t => Base(t, TextExtractionMetadata.TesseractSlug) with
            {
                Language = t.Language,
                Dpi = t.Dpi,
                Psm = t.Psm,
                Oem = t.Oem,
                PagesFromTextLayer = t.PagesFromTextLayer,
                PagesOcred = t.PagesOcred
            },
            AzureDocumentIntelligenceTextExtractionMetadata a => Base(a, TextExtractionMetadata.AzureDocumentIntelligenceSlug) with
            {
                ModelId = a.ModelId,
                OutputContentFormat = a.OutputContentFormat,
                PageCount = a.PageCount
            },
            _ => null
        };
    }

    private static TextExtractionProvenanceResponse Base(TextExtractionMetadata metadata, string engine) => new()
    {
        Engine = engine,
        EngineKey = metadata.EngineKey,
        ExtractedAt = metadata.ExtractedAt,
        RequestedEngineKey = metadata.RequestedEngineKey,
        FallbackReason = metadata.FallbackReason
    };
}
