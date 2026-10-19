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

    // --- Azure Document Intelligence ---

    // Resource endpoint, e.g. https://my-docint.cognitiveservices.azure.com.
    public string? Endpoint { get; set; }

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
