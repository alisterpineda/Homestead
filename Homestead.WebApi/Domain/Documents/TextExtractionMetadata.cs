using System.Text.Json;
using System.Text.Json.Serialization;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Domain.Documents;

/// <summary>
/// Provenance for a version's stored text, persisted as jsonb on <see cref="DocumentVersion"/>. Polymorphic
/// over KIND, following the <c>FieldChange</c> precedent — the discriminator is the engine TYPE slug
/// ("tesseract") for an engine run, or "manual" for text a person hand-edited. Engine kinds carry the
/// configured instance that ran in <see cref="EngineTextExtractionMetadata.EngineKey"/> ("azure-layout"):
/// two engines of the same type differ only by config, so they share a shape.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = DiscriminatorPropertyName)]
[JsonDerivedType(typeof(TesseractTextExtractionMetadata), TesseractSlug)]
[JsonDerivedType(typeof(AzureDocumentIntelligenceTextExtractionMetadata), AzureDocumentIntelligenceSlug)]
[JsonDerivedType(typeof(DoclingTextExtractionMetadata), DoclingSlug)]
[JsonDerivedType(typeof(ManualTextExtractionMetadata), ManualSlug)]
public abstract record TextExtractionMetadata
{
    public const string DiscriminatorPropertyName = "engine";
    public const string TesseractSlug = "tesseract";
    public const string AzureDocumentIntelligenceSlug = "azure-document-intelligence";
    public const string DoclingSlug = "docling";
    public const string ManualSlug = "manual";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// When the stored text came to be: the engine run for an engine kind, the edit for
    /// <see cref="ManualTextExtractionMetadata"/>. Either way it changes exactly when the text does, which is
    /// what lets the detail page key its editor seed on it.
    /// </summary>
    public required DateTimeOffset ExtractedAt { get; init; }

    /// <summary>The discriminator slug for a record — the one <see cref="Serialize"/> writes.</summary>
    public static string SlugOf(TextExtractionMetadata metadata) => metadata switch
    {
        TesseractTextExtractionMetadata => TesseractSlug,
        AzureDocumentIntelligenceTextExtractionMetadata => AzureDocumentIntelligenceSlug,
        DoclingTextExtractionMetadata => DoclingSlug,
        ManualTextExtractionMetadata => ManualSlug,
        _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata.GetType(), "Unregistered provenance kind.")
    };

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
                DoclingSlug => document.RootElement.Deserialize<DoclingTextExtractionMetadata>(JsonOptions),
                ManualSlug => document.RootElement.Deserialize<ManualTextExtractionMetadata>(JsonOptions),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// What every engine run records, whichever engine type ran. Only engine kinds have an instance key and
/// a fall-back story; a manual edit has neither, which is why these live here and not on the root.
/// </summary>
public abstract record EngineTextExtractionMetadata : TextExtractionMetadata
{
    /// <summary>The configured engine instance that actually ran.</summary>
    public required string EngineKey { get; init; }

    /// <summary>
    /// Set only when the engine that ran is not the one that was asked for — i.e. the requested engine
    /// was unavailable and the built-in Tesseract engine ran instead. This is what keeps the default
    /// path's fall-back honest: the substitution is recorded rather than silent.
    /// </summary>
    public string? RequestedEngineKey { get; init; }

    /// <summary>Why <see cref="RequestedEngineKey"/> could not run.</summary>
    public string? FallbackReason { get; init; }
}

public record TesseractTextExtractionMetadata : EngineTextExtractionMetadata
{
    public const string PopplerTextLayer = "poppler";
    public const string PdfPigTextLayer = "pdfpig";

    /// <summary>The tesseract -l argument, e.g. "eng" or "eng+fra".</summary>
    public required string Language { get; init; }

    public int Dpi { get; init; }
    public int Psm { get; init; }
    public int Oem { get; init; }

    /// <summary>
    /// Pages whose text came from the PDF's embedded text layer, and pages that were rasterized and OCR'd.
    /// The highest-value fields here: the hybrid PDF path silently picks one branch per page, and without
    /// these the choice is unknowable after the fact without re-running the extraction. A born-digital
    /// bank statement showing PagesOcred: 0 is the proof that its text came from the embedded text layer
    /// (read by <see cref="TextLayerExtractor"/>) rather than from OCR.
    /// </summary>
    public int PagesFromTextLayer { get; init; }

    public int PagesOcred { get; init; }

    /// <summary>
    /// Which tool read a PDF's embedded text layer: <see cref="PopplerTextLayer"/> (<c>pdftotext -layout</c>,
    /// column alignment kept) or <see cref="PdfPigTextLayer"/> (the fallback when pdftotext is missing,
    /// fails, or disagrees with PdfPig on the page count — columns flattened into prose). Null for images,
    /// where no text-layer pass runs, and for every version extracted before this field existed.
    /// </summary>
    /// <remarks>
    /// Deliberately nullable and NOT <c>required</c>: <see cref="TextExtractionMetadata.Deserialize"/> binds
    /// this record directly, and a required member missing from an existing jsonb blob would throw —
    /// which Deserialize turns into null, erasing every pre-existing version's provenance.
    /// </remarks>
    public string? TextLayerExtractor { get; init; }
}

public record AzureDocumentIntelligenceTextExtractionMetadata : EngineTextExtractionMetadata
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

public record DoclingTextExtractionMetadata : EngineTextExtractionMetadata
{
    public const string SuccessStatus = "success";
    public const string PartialSuccessStatus = "partial_success";

    // Ours, not docling-serve's: the file is a type Docling does not convert, so no request was made.
    public const string UnsupportedContentTypeStatus = "unsupported_content_type";

    /// <summary>
    /// The docling-serve version that ran, when the sidecar reports it. Null when its <c>/version</c> is
    /// disabled (<c>DOCLING_SERVE_SHOW_VERSION_INFO=false</c>) or could not be read.
    /// </summary>
    public string? ServerVersion { get; init; }

    /// <summary>The table_mode sent ("fast" or "accurate"); null when the server default (accurate) applied.</summary>
    public string? TableMode { get; init; }

    public bool ForceOcr { get; init; }

    /// <summary>
    /// Source pages, counted locally with PdfPig (1 for an image) — the Markdown response carries no page
    /// count. Zero when the PDF could not be read.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// docling-serve's conversion status: "success" or "partial_success" (a stored text only ever comes
    /// from one of those), or <see cref="UnsupportedContentTypeStatus"/> when no conversion was attempted.
    /// "partial_success" is the durable signal that some pages or tables failed to convert and the text is
    /// incomplete. Required: every Docling row has one, and the record is new, so there is no older blob
    /// without it.
    /// </summary>
    public required string ConversionStatus { get; init; }

    /// <summary>Server-side processing time, as docling-serve reports it.</summary>
    public double? ProcessingSeconds { get; init; }
}

/// <summary>
/// The text was hand-edited by a person. Replaces an engine's record rather than erasing it: the editor
/// and the edit time are what the version now shows, and the engine run the edit started from survives
/// as the <i>basis</i>, so "edited Docling output" never reads as "Docling output" or as "never extracted".
/// </summary>
/// <remarks>
/// Nothing here is <c>required</c> beyond the root's <see cref="TextExtractionMetadata.ExtractedAt"/>
/// (the edit time): <see cref="TextExtractionMetadata.Deserialize"/> binds this record directly, and a
/// required member missing from an older blob would erase the row's provenance (see
/// <see cref="TesseractTextExtractionMetadata.TextLayerExtractor"/>).
/// </remarks>
public sealed record ManualTextExtractionMetadata : TextExtractionMetadata
{
    /// <summary>Who saved the edit. Nullable because the current user accessor's id is.</summary>
    public Guid? EditedByUserId { get; init; }

    /// <summary>
    /// The engine run the edit started from: its type slug, the instance that ran, and when. All null when
    /// the text was entered with no prior extraction (or over a version whose provenance was unknown).
    /// </summary>
    public string? BasisEngine { get; init; }

    public string? BasisEngineKey { get; init; }

    public DateTimeOffset? BasisExtractedAt { get; init; }

    /// <summary>
    /// The record that replaces <paramref name="previous"/> after a hand edit. An engine run becomes the
    /// basis; an earlier manual record passes its basis through unchanged, so a second correction is still
    /// "edited Docling output" rather than "edited an edit"; no provenance means no basis.
    /// </summary>
    public static ManualTextExtractionMetadata Replacing(
        TextExtractionMetadata? previous, Guid? editedByUserId, DateTimeOffset editedAt) => previous switch
    {
        EngineTextExtractionMetadata engine => new()
        {
            ExtractedAt = editedAt,
            EditedByUserId = editedByUserId,
            BasisEngine = SlugOf(engine),
            BasisEngineKey = engine.EngineKey,
            BasisExtractedAt = engine.ExtractedAt
        },
        ManualTextExtractionMetadata manual => manual with
        {
            ExtractedAt = editedAt,
            EditedByUserId = editedByUserId
        },
        _ => new()
        {
            ExtractedAt = editedAt,
            EditedByUserId = editedByUserId
        }
    };
}
