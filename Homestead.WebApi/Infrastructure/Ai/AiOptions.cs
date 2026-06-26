namespace Homestead.WebApi.Infrastructure.Ai;

// Static configuration for the single, optional AI integration. Mirrors Authentication/OidcOptions —
// a plain POCO bound from the "Ai" config section with an IsConfigured probe and no startup validator:
// AI is optional, so an unconfigured app degrades to a graceful no-op rather than failing to start.
public class AiOptions
{
    public const string SectionName = "Ai";

    // Ollama's OpenAI-compatible base, e.g. http://homeserver:11434/v1.
    public string? Endpoint { get; set; }

    // The Ollama model tag, e.g. "gemma-4-e4b".
    public string? Model { get; set; }

    // Optional; Ollama ignores it. A dummy key is substituted when this is blank.
    public string? ApiKey { get; set; }

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
        MaxInputChars is int n && n >= MinInputChars ? n : DefaultMaxInputChars;

    // Kill-switch for attaching a rendered page-1 image to the classify call (in addition to OCR text +
    // catalogs). Defaults on; flip to false to fall back to text-only classification even when AI is
    // configured (e.g. to skip the slow vision prefill, or while debugging).
    public bool ImageInput { get; set; } = true;

    // Long-edge pixel budget for the dedicated AI page render. The image cost flattens at ~1024px on the
    // target endpoint and larger renders don't add tokens, so this is the only fidelity lever — exposed
    // for future tuning once an eval set exists. Clamped by ResolveImageRenderMaxEdge.
    public int ImageRenderMaxEdge { get; set; } = DefaultImageRenderMaxEdge;

    // Default long-edge budget, applied when ImageRenderMaxEdge is below the floor (treated as "unset").
    public const int DefaultImageRenderMaxEdge = 1024;

    // Floor for a configured render edge. A value below this is treated as "unset" and falls back to the
    // default, so a degenerate tiny value can never starve the render of legible detail.
    public const int MinImageRenderMaxEdge = 256;

    // Ceiling for a configured render edge. Bounds memory/decode cost; values above are clamped down.
    public const int MaxImageRenderMaxEdge = 4096;

    // Resolves the effective render long-edge: a sub-floor value falls back to the default, anything else
    // is clamped to the ceiling. Mirrors ResolveMaxInputChars's "below floor => default" posture.
    public int ResolveImageRenderMaxEdge() =>
        ImageRenderMaxEdge < MinImageRenderMaxEdge
            ? DefaultImageRenderMaxEdge
            : Math.Min(ImageRenderMaxEdge, MaxImageRenderMaxEdge);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Model);
}
