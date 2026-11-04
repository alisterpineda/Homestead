using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Documents.Features.Versions;

/// <summary>
/// How a version's stored text was produced. Deliberately FLAT rather than mirroring the persisted
/// <see cref="TextExtractionMetadata"/> union: a polymorphic C# hierarchy generates an awkward
/// discriminated union through ng-openapi-gen, and this is read-only display data. <see cref="Engine"/>
/// carries the kind slug, and the kind-specific fields are simply null for the kinds that did not apply.
/// </summary>
public record TextExtractionProvenanceResponse
{
    /// <summary>
    /// Kind slug — "tesseract", "azure-document-intelligence" or "docling" for an engine run, or "manual"
    /// when a person hand-edited the text.
    /// </summary>
    public required string Engine { get; init; }

    /// <summary>The configured engine instance that ran, e.g. "azure-layout". Null for "manual".</summary>
    public string? EngineKey { get; init; }

    /// <summary>When the stored text came to be: the engine run, or the edit for "manual".</summary>
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

    /// <summary>"poppler" or "pdfpig" — which tool read a PDF's text layer. Null for images.</summary>
    public string? TextLayerExtractor { get; init; }

    // --- Azure Document Intelligence ---
    public string? ModelId { get; init; }
    public AzureOutputContentFormat? OutputContentFormat { get; init; }

    /// <summary>
    /// Shared by Azure and Docling, with different sources: Azure = pages it reports having analyzed,
    /// Docling = source pages counted locally.
    /// </summary>
    public int? PageCount { get; init; }

    // --- Docling ---
    public string? ServerVersion { get; init; }

    /// <summary>"fast" or "accurate"; null when the server default (accurate) applied.</summary>
    public string? TableMode { get; init; }

    public bool? ForceOcr { get; init; }

    /// <summary>
    /// One of <see cref="DoclingTextExtractionMetadata.SuccessStatus"/> ("success"),
    /// <see cref="DoclingTextExtractionMetadata.PartialSuccessStatus"/> ("partial_success": some content
    /// could not be converted, so the text is incomplete), or
    /// <see cref="DoclingTextExtractionMetadata.UnsupportedContentTypeStatus"/> ("unsupported_content_type":
    /// a file type Docling does not convert, so no conversion ran and there is no text).
    /// </summary>
    public string? ConversionStatus { get; init; }

    public double? ProcessingSeconds { get; init; }

    // --- Manual ---

    /// <summary>
    /// Who hand-edited the text, resolved from the stored user id at read time (never persisted, so a
    /// rename stays correct). Null on the pure mapping; the GET handlers fill it in.
    /// </summary>
    public string? EditedByDisplayName { get; init; }

    /// <summary>
    /// The engine run the edit started from — its kind slug, instance key and time. All null when the
    /// text was entered with no prior extraction.
    /// </summary>
    public string? BasisEngine { get; init; }

    public string? BasisEngineKey { get; init; }

    public DateTimeOffset? BasisExtractedAt { get; init; }

    /// <summary>
    /// Maps the persisted jsonb. Returns null for a version extracted before provenance existed, and
    /// also for a blob that no longer deserializes — a shape that has moved on is not worth failing a
    /// read for when the payload is display-only.
    /// </summary>
    public static TextExtractionProvenanceResponse? From(string? json) =>
        From(TextExtractionMetadata.Deserialize(json));

    public static TextExtractionProvenanceResponse? From(TextExtractionMetadata? metadata)
    {
        return metadata switch
        {
            TesseractTextExtractionMetadata t => FromEngine(t) with
            {
                Language = t.Language,
                Dpi = t.Dpi,
                Psm = t.Psm,
                Oem = t.Oem,
                PagesFromTextLayer = t.PagesFromTextLayer,
                PagesOcred = t.PagesOcred,
                TextLayerExtractor = t.TextLayerExtractor
            },
            AzureDocumentIntelligenceTextExtractionMetadata a => FromEngine(a) with
            {
                ModelId = a.ModelId,
                OutputContentFormat = a.OutputContentFormat,
                PageCount = a.PageCount
            },
            DoclingTextExtractionMetadata d => FromEngine(d) with
            {
                ServerVersion = d.ServerVersion,
                TableMode = d.TableMode,
                ForceOcr = d.ForceOcr,
                PageCount = d.PageCount,
                ConversionStatus = d.ConversionStatus,
                ProcessingSeconds = d.ProcessingSeconds
            },
            ManualTextExtractionMetadata m => new TextExtractionProvenanceResponse
            {
                Engine = TextExtractionMetadata.ManualSlug,
                ExtractedAt = m.ExtractedAt,
                BasisEngine = m.BasisEngine,
                BasisEngineKey = m.BasisEngineKey,
                BasisExtractedAt = m.BasisExtractedAt
            },
            _ => null
        };
    }

    private static TextExtractionProvenanceResponse FromEngine(EngineTextExtractionMetadata metadata) => new()
    {
        Engine = TextExtractionMetadata.SlugOf(metadata),
        EngineKey = metadata.EngineKey,
        ExtractedAt = metadata.ExtractedAt,
        RequestedEngineKey = metadata.RequestedEngineKey,
        FallbackReason = metadata.FallbackReason
    };
}
