using System.Text.Json;
using System.Text.Json.Serialization;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Domain.Documents;

/// <summary>
/// Provenance for one text extraction, persisted as jsonb on <see cref="DocumentVersion"/>. Polymorphic
/// over engine type, following the <c>FieldChange</c> precedent — the discriminator is the engine TYPE
/// slug ("tesseract"), while the configured instance that ran is the <see cref="EngineKey"/> field
/// ("azure-layout"). Two engines of the same type differ only by config, so they share a shape.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = DiscriminatorPropertyName)]
[JsonDerivedType(typeof(TesseractTextExtractionMetadata), TesseractSlug)]
[JsonDerivedType(typeof(AzureDocumentIntelligenceTextExtractionMetadata), AzureDocumentIntelligenceSlug)]
public abstract record TextExtractionMetadata
{
    public const string DiscriminatorPropertyName = "engine";
    public const string TesseractSlug = "tesseract";
    public const string AzureDocumentIntelligenceSlug = "azure-document-intelligence";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The configured engine instance that actually ran.</summary>
    public required string EngineKey { get; init; }

    public required DateTimeOffset ExtractedAt { get; init; }

    /// <summary>
    /// Set only when the engine that ran is not the one that was asked for — i.e. the requested engine
    /// was unavailable and the built-in Tesseract engine ran instead. This is what keeps the default
    /// path's fall-back honest: the substitution is recorded rather than silent.
    /// </summary>
    public string? RequestedEngineKey { get; init; }

    /// <summary>Why <see cref="RequestedEngineKey"/> could not run.</summary>
    public string? FallbackReason { get; init; }

    /// <summary>
    /// Serializes to the jsonb column's shape. Declared as the base type on purpose — that is what makes
    /// System.Text.Json emit the "engine" discriminator.
    /// </summary>
    public static string? Serialize(TextExtractionMetadata? metadata) =>
        metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions);

    /// <summary>
    /// Reads the jsonb column back. Returns null for absent, unreadable, or unrecognized payloads —
    /// versions predate this column, and provenance is display-only data that is not worth failing a
    /// read for.
    /// </summary>
    /// <remarks>
    /// The discriminator is read explicitly rather than letting System.Text.Json's polymorphic
    /// deserializer find it, because <b>this value round-trips through a Postgres jsonb column and jsonb
    /// does not preserve key order</b> — it stores keys sorted by (length, bytes). "dpi"/"oem"/"psm" are
    /// therefore returned <i>before</i> "engine", and STJ requires the discriminator to be the FIRST
    /// property; anything else throws NotSupportedException. Dispatching by hand makes the read
    /// order-independent, which is the only thing that makes a polymorphic payload safe in jsonb at all.
    /// (Domain/Auditing/FieldChange survives the same trap only by accident: its "$type" is the shortest
    /// key it has and "$" sorts low.)
    /// </remarks>
    public static TextExtractionMetadata? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty(DiscriminatorPropertyName, out var discriminator)
                || discriminator.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            // Bound to the CONCRETE type, so the leftover "engine" property simply has no matching member
            // and is skipped (System.Text.Json's default for unmapped members).
            return discriminator.GetString() switch
            {
                TesseractSlug => document.RootElement.Deserialize<TesseractTextExtractionMetadata>(JsonOptions),
                AzureDocumentIntelligenceSlug =>
                    document.RootElement.Deserialize<AzureDocumentIntelligenceTextExtractionMetadata>(JsonOptions),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public record TesseractTextExtractionMetadata : TextExtractionMetadata
{
    /// <summary>The tesseract -l argument, e.g. "eng" or "eng+fra".</summary>
    public required string Language { get; init; }

    public int Dpi { get; init; }
    public int Psm { get; init; }
    public int Oem { get; init; }

    /// <summary>
    /// Pages whose text came from the PDF's embedded text layer, and pages that were rasterized and OCR'd.
    /// The highest-value fields here: the hybrid PDF path silently picks one branch per page, and without
    /// these the choice is unknowable after the fact without re-running the extraction. A born-digital
    /// bank statement showing PagesOcred: 0 is the proof that its badly-spaced text came from PdfPig's
    /// page.Text rather than from OCR.
    /// </summary>
    public int PagesFromTextLayer { get; init; }

    public int PagesOcred { get; init; }
}

public record AzureDocumentIntelligenceTextExtractionMetadata : TextExtractionMetadata
{
    public required string ModelId { get; init; }

    public required AzureOutputContentFormat OutputContentFormat { get; init; }

    /// <summary>
    /// Pages Azure reports having analyzed. Billing visibility, and the durable signal for F0-tier
    /// truncation: a free resource analyzes only the first 2 pages and reports success, so a 2 here
    /// against a 30-page statement is the only evidence the text is incomplete.
    /// </summary>
    public int PageCount { get; init; }
}
