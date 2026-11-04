using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// One options bag for every configured engine instance, discriminated by Type. Type-specific fields are
// validated per Type (see TextExtractionEngineValidation) rather than split into separate classes: the
// config binder needs a single concrete type to bind each "Ocr:Engines:{key}" section onto, and the
// alternative (bind twice, once per shape) buys nothing when the whole bag is a dozen fields.
//
// Type is bound by the CONFIG binder's own Enum.Parse — it never round-trips through System.Text.Json,
// which is why engines are selected by string key everywhere else (payloads, JSON Patch, jsonb).
public class OcrEngineOptions
{
    public const string SectionName = "Ocr:Engines";

    // Which engine uploads and un-pinned reprocess runs use — an "Ocr:Engines:{key}" name. Unset means
    // the built-in engine. Deployment config, like the engines themselves.
    public const string DefaultEngineConfigKey = "Ocr:DefaultEngine";

    // The always-present engine. It is the terminus of the resolution chain (explicit key ->
    // Ocr:DefaultEngine -> this), so it is seeded even when config defines no engines at all.
    public const string BuiltInTesseractKey = "tesseract";

    // Nullable so "not configured" is distinguishable from Tesseract. A non-nullable enum would bind an
    // omitted Type to default(0) = Tesseract, so an operator who sets an Azure Endpoint + ApiKey but
    // forgets Type would get a fully-available engine that silently runs local Tesseract under the name
    // "azure-layout" — precisely the silent substitution the rest of this design refuses to make.
    // Required for every instance except the built-in key; see ResolveType.
    public TextExtractionEngineType? Type { get; set; }

    // The built-in "tesseract" instance is the one engine whose type is implied by its name, so config
    // that only overrides its TessDataPath (as the Docker image does) need not restate the type.
    public TextExtractionEngineType? ResolveType(string key) =>
        Type ?? (string.Equals(key, BuiltInTesseractKey, StringComparison.OrdinalIgnoreCase)
            ? TextExtractionEngineType.Tesseract
            : null);

    // --- Any type ---

    // How long a job that runs this engine may take — the budget the OCR job handlers declare to the job
    // runner (IJobHandler.ResolveTimeout) in place of the global Processing:JobTimeout, which applies when
    // this is unset. TextExtractionHelper stops the engine a margin short of it, so a slow extraction
    // degrades to "no text" instead of the whole job timing out. Set it for engines that are slow by
    // nature (a CPU-bound sidecar on a large scan); a value below the global budget shortens the job.
    //
    // A TimeSpan, so "00:20:00". A bare number binds as DAYS ("600" is 600 days), hence MaxJobTimeout.
    public TimeSpan? JobTimeout { get; set; }

    // The longest JobTimeout accepted. Well past any real extraction, and far inside what
    // CancellationTokenSource.CancelAfter accepts (~49.7 days), which would otherwise throw on every job.
    public static readonly TimeSpan MaxJobTimeout = TimeSpan.FromDays(1);

    // A JobTimeout fit to run under: set, positive, and within MaxJobTimeout. Anything else is reported
    // by validation, and treated as unset by everything that schedules a deadline from it — the built-in
    // engine runs even when its own validation fails.
    public static TimeSpan? UsableJobTimeout(TimeSpan? value) =>
        value > TimeSpan.Zero && value <= MaxJobTimeout ? value : null;

    // --- Tesseract ---

    public string TessDataPath { get; set; } = "tessdata";

    // OCR languages, joined into Tesseract's "eng+fra" form at extraction time. Empty means English.
    //
    // Deliberately NOT seeded with [Eng]: the config binder ADDS to an existing collection rather than
    // replacing it, so a pre-seeded default would make configured languages append to it — ["Fra"] in
    // config would bind to [Eng, Fra] and run as "eng+fra", silently changing the primary language.
    // Starting empty means what config says is what you get. The English default lives in
    // TesseractTextExtractionEngine.ResolveLanguage instead.
    public List<TesseractLanguage> Languages { get; set; } = [];

    // DPI used to rasterize PDF pages, and passed as --dpi for those rendered pages.
    public int Dpi { get; set; } = 300;

    // Tesseract page segmentation mode (--psm). 3 = fully automatic, correct for multi-block documents.
    public int Psm { get; set; } = 3;

    // Tesseract OCR engine mode (--oem). 3 = default (best engine available); never errors on engine selection.
    public int Oem { get; set; } = 3;

    // --- Azure Document Intelligence, and Docling ---

    // Azure: the resource endpoint, e.g. https://my-docint.cognitiveservices.azure.com.
    // Docling: the docling-serve base URL, e.g. http://docling:5001.
    public string? Endpoint { get; set; }

    // Azure: required. Docling: optional — sent as X-Api-Key, and only needed when the sidecar sets
    // DOCLING_SERVE_API_KEY.
    public string? ApiKey { get; set; }

    // The analysis model. One Azure resource serves several meaningfully different models, which is why
    // engines are named config instances rather than one-per-type: "azure-layout" and "azure-read" are
    // both legitimate engines over the same credentials.
    public string ModelId { get; set; } = AzureModels.PrebuiltLayout;

    // Unset resolves to Markdown for prebuilt-layout (its whole point — HTML tables survive) and Text
    // for everything else, which is the only format those models support.
    public AzureOutputContentFormat? OutputContentFormat { get; set; }

    public AzureOutputContentFormat ResolveOutputContentFormat() =>
        OutputContentFormat ?? (AzureModels.SupportsMarkdown(ModelId)
            ? AzureOutputContentFormat.Markdown
            : AzureOutputContentFormat.Text);

    // --- Docling ---

    // docling-serve's table_mode: "fast" or "accurate". Null sends nothing, leaving the server default
    // (accurate). Fast trades table-structure quality for speed on CPU.
    public string? TableMode { get; set; }

    // Sent as force_ocr: OCR every page even when it has a text layer. Off by default — a born-digital PDF's
    // embedded text is exact, OCR of it is not.
    public bool ForceOcr { get; set; }
}

public static class DoclingTableModes
{
    public const string Fast = "fast";
    public const string Accurate = "accurate";

    public static bool IsValid(string? mode) =>
        string.Equals(mode, Fast, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, Accurate, StringComparison.OrdinalIgnoreCase);
}

public static class AzureModels
{
    public const string PrebuiltLayout = "prebuilt-layout";
    public const string PrebuiltRead = "prebuilt-read";

    // Markdown is what emits the <table>/<tr>/<td> syntax that makes statement parsing work, and only
    // prebuilt-layout produces it. Matched exactly, not by prefix: Azure nests real ids under a shared
    // prefix elsewhere (prebuilt-tax.us, prebuilt-tax.us.w2), so a prefix would accept ids that are not
    // layout at all.
    //
    // That markdown is layout-only is INFERRED from where Microsoft's docs place the feature, not stated
    // by them: outputContentFormat is documented on the generic Analyze operation with no per-model note,
    // and they did document the equivalent exclusivity for output=pdf. ValidateAzure enforces the
    // inference. A single curl (prebuilt-read + outputContentFormat=markdown) would settle it.
    public static bool SupportsMarkdown(string? modelId) =>
        string.Equals(modelId, PrebuiltLayout, StringComparison.OrdinalIgnoreCase);
}
