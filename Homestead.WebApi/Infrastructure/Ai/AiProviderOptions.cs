namespace Homestead.WebApi.Infrastructure.Ai;

// One options bag for every configured provider instance, discriminated by Type — the config binder needs
// a single concrete type to bind each "Ai:Providers:{key}" section onto (same shape as OcrEngineOptions).
// Per-type required fields are validated in AiProviderConfiguration rather than split into subclasses.
public class AiProviderOptions
{
    public const string SectionName = "Ai:Providers";

    // Nullable so "not configured" is distinguishable from OpenAiCompatible, and — unlike the OCR bag —
    // required for EVERY key: there is no built-in provider whose type its name could imply. A non-nullable
    // enum would bind an omitted Type to default(0) = OpenAiCompatible, so an operator who configures an
    // Azure endpoint but forgets Type would get a provider that quietly speaks the wrong protocol under the
    // name they chose — the silent substitution this design refuses to make.
    public AiProviderType? Type { get; set; }

    // OpenAiCompatible: the API base URL, e.g. http://homeserver:11434/v1.
    // AzureFoundry: the resource endpoint, e.g. https://my-resource.openai.azure.com.
    // Anthropic: optional base-URL override; blank uses the SDK default (the Claude API).
    public string? Endpoint { get; set; }

    // OpenAiCompatible: the model tag, e.g. "gemma-4-e4b". AzureFoundry: the DEPLOYMENT name.
    // Anthropic: the Claude model ID, e.g. "claude-opus-5-5".
    public string? Model { get; set; }

    // Optional for OpenAiCompatible (Ollama ignores it; a dummy key is substituted when blank).
    // Required for AzureFoundry and Anthropic.
    public string? ApiKey { get; set; }

    // --- Per-call knobs. Model/endpoint traits, so they live on the provider, not the app. ---

    // Optional override for the document-text character budget embedded in the classification prompt.
    public int? MaxInputChars { get; set; }

    // Default character budget for the document text embedded in the classification prompt, applied when
    // MaxInputChars is unset. The model is sent a head+tail sample of the extracted text capped at this many
    // characters.
    public const int DefaultMaxInputChars = 12_000;

    // Floor for a configured budget. A value below this is treated as "unset" and falls back to
    // DefaultMaxInputChars, so a tiny budget can never starve the prompt of document signal.
    public const int MinInputChars = 1_000;

    // Resolves the effective character budget: the configured value when it clears the floor, else the
    // default.
    public int ResolveMaxInputChars() =>
        ResolveWithFloor(MaxInputChars, MinInputChars, DefaultMaxInputChars);

    // Optional override for the statement-extraction character budget. Statement parsing must see the FULL
    // text (the closing balance can sit anywhere in the document), so it does NOT use the classification
    // head+tail sampler — the text is sent whole and only hard-truncated at this cap (which is logged).
    public int? StatementMaxInputChars { get; set; }

    // Default statement-extraction budget. Deliberately an order of magnitude larger than the
    // classification budget so a full multi-page statement fits without sampling. Page-chunking for
    // statements longer than this is a deferred follow-up.
    public const int DefaultStatementMaxInputChars = 120_000;

    // Resolves the effective statement budget: the configured value when it clears the shared floor, else
    // the statement default.
    public int ResolveStatementMaxInputChars() =>
        ResolveWithFloor(StatementMaxInputChars, MinInputChars, DefaultStatementMaxInputChars);

    // Kill-switch for attaching a rendered page-1 image to the classify call (in addition to OCR text +
    // catalogs). Defaults on; flip to false for a text-only model, or to skip the slow vision prefill.
    public bool ImageInput { get; set; } = true;

    // Long-edge pixel budget for the dedicated AI page render. The image cost typically flattens around
    // ~1024px and larger renders don't add tokens, so this is the only fidelity lever — exposed for future
    // tuning once an eval set exists. Clamped by ResolveImageRenderMaxEdge.
    public int ImageRenderMaxEdge { get; set; } = DefaultImageRenderMaxEdge;

    // Default long-edge budget, applied when ImageRenderMaxEdge is below the floor (treated as "unset").
    public const int DefaultImageRenderMaxEdge = 1024;

    // Floor for a configured render edge. A value below this is treated as "unset" and falls back to the
    // default, so a degenerate tiny value can never starve the render of legible detail.
    public const int MinImageRenderMaxEdge = 256;

    // Ceiling for a configured render edge. Bounds memory/decode cost; values above are clamped down.
    public const int MaxImageRenderMaxEdge = 4096;

    // Resolves the effective render long-edge: a sub-floor value falls back to the default, anything else
    // is clamped to the ceiling.
    public int ResolveImageRenderMaxEdge() =>
        ResolveWithFloor(ImageRenderMaxEdge, MinImageRenderMaxEdge, DefaultImageRenderMaxEdge,
            MaxImageRenderMaxEdge);

    // The one floor policy every knob shares: unset or below the floor is "unset" and falls back to the
    // default (so a degenerate tiny value never starves the feature); a ceiling, when given, clamps down.
    private static int ResolveWithFloor(int? value, int floor, int fallback, int? ceiling = null) =>
        value is int n && n >= floor
            ? Math.Min(n, ceiling ?? n)
            : fallback;
}
