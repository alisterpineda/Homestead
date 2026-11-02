using System.Numerics;

namespace Homestead.WebApi.Infrastructure.Ai;

// The one "Ai" config section: the Claude API key and model, the effort every request carries, the refusal
// fallback switch, and the consumer-side input knobs. Validated by AiConfiguration into a FeatureAvailability (no
// ValidateOnStart — see there); the knobs are resolved once into AiInputSettings.
public class AiOptions
{
    public const string SectionName = "Ai";

    // Required. Never surfaced — the admin status page shows only whether one is set.
    public string? ApiKey { get; set; }

    // Required. A Claude model ID, e.g. "claude-opus-5-5".
    public string? Model { get; set; }

    // Optional base-URL override; blank uses the SDK default (the Claude API). Must be an absolute URI when set.
    public string? Endpoint { get; set; }

    // low | medium | high | xhigh | max, case-insensitive; blank = medium (the API's own default on Opus 5.5, so
    // leaving it unset changes nothing). Carried on every request. Unknown values make AI unavailable-with-reason.
    public string? Effort { get; set; }

    // Server-side fallback on a classifier refusal: a refused request is re-run, inside the same call, on a model
    // that does not run that classifier (Anthropic maintains the mapping). Both attempts are billed. False sends no
    // fallback, so a refusal is a final AiResponseIncompleteException.
    public bool RefusalFallback { get; set; } = true;

    // Optional override for the document-text character budget embedded in the classification prompt.
    public int? MaxInputChars { get; set; }

    // Default character budget for the document text embedded in the classification prompt, applied when
    // MaxInputChars is unset. The model is sent a head+tail sample of the extracted text capped at this many
    // characters.
    public const int DefaultMaxInputChars = 12_000;

    // Floor for a configured budget. A value below this is treated as "unset" and falls back to
    // DefaultMaxInputChars, so a tiny budget can never starve the prompt of document signal.
    public const int MinInputChars = 1_000;

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

    public int ResolveStatementMaxInputChars() =>
        ResolveWithFloor(StatementMaxInputChars, MinInputChars, DefaultStatementMaxInputChars);

    // Whether to attach a rendered page-1 image to the classify call (in addition to OCR text + catalogs). Every
    // current Claude model reads images, so this is a cost knob: the image is the bulk of a classify's input tokens.
    public bool ImageInput { get; set; } = true;

    // Long-edge pixel budget for the dedicated AI page render. The image cost typically flattens around
    // ~1024px and larger renders don't add tokens, so this is the only fidelity lever. Clamped by
    // ResolveImageRenderMaxEdge.
    public int ImageRenderMaxEdge { get; set; } = DefaultImageRenderMaxEdge;

    public const int DefaultImageRenderMaxEdge = 1024;

    // A configured edge below this is treated as "unset" and falls back to the default, so a degenerate tiny
    // value can never starve the render of legible detail.
    public const int MinImageRenderMaxEdge = 256;

    // Bounds memory/decode cost; values above are clamped down.
    public const int MaxImageRenderMaxEdge = 4096;

    public int ResolveImageRenderMaxEdge() =>
        ResolveWithFloor(ImageRenderMaxEdge, MinImageRenderMaxEdge, DefaultImageRenderMaxEdge,
            MaxImageRenderMaxEdge);

    // Whether classification may send the original PDF instead of the OCR text and page render (Claude reads the
    // text and layout of every page itself). Gated per document by scope, page count, size and encryption
    // (ClassificationInputSelector); a rejected PDF falls back to text + render once. One line turns it off.
    public bool PdfInput { get; set; } = true;

    // Pages drive tokens, so a page cap is mandatory: a longer PDF takes the text + render path.
    public int PdfInputMaxPages { get; set; } = DefaultPdfInputMaxPages;

    public const int DefaultPdfInputMaxPages = 20;
    public const int MinPdfInputMaxPages = 1;

    // The Claude API's PDF page limit on its smallest-context models; values above are clamped down.
    public const int MaxPdfInputMaxPages = 100;

    public int ResolvePdfInputMaxPages() =>
        ResolveWithFloor(PdfInputMaxPages, MinPdfInputMaxPages, DefaultPdfInputMaxPages, MaxPdfInputMaxPages);

    // Whether a PDF goes to the Claude API as an upload to its Files API (referenced by file_id, deleted after the
    // call, and set to expire after an hour in case the delete never runs) rather than base64 inside the request.
    // Uploading keeps the request a few KB and its memory near the file's size; inline base64 holds ~20x the file
    // size during the call. Off is for an Ai:Endpoint proxy that doesn't serve /v1/files.
    public bool PdfUpload { get; set; } = true;

    // A larger PDF takes the text + render path. Unset, the cap follows PdfUpload: 20 MiB when uploading, 4 MiB
    // inline, where the ~20x memory cost bites (bulk classify runs several at once). The ceiling keeps an inline
    // document's base64 (4/3 of the file) well inside the Claude API's 32 MB request limit, and large PDFs can fail
    // even through the Files API. Household documents are rarely larger.
    public long? PdfInputMaxBytes { get; set; }

    public const long DefaultUploadPdfInputMaxBytes = 20L * 1024 * 1024;
    public const long DefaultInlinePdfInputMaxBytes = 4L * 1024 * 1024;
    public const long MinPdfInputMaxBytes = 64L * 1024;
    public const long MaxPdfInputMaxBytes = 20L * 1024 * 1024;

    public long ResolvePdfInputMaxBytes() =>
        ResolveWithFloor(PdfInputMaxBytes, MinPdfInputMaxBytes,
            PdfUpload ? DefaultUploadPdfInputMaxBytes : DefaultInlinePdfInputMaxBytes, MaxPdfInputMaxBytes);

    // The one floor policy every knob shares: unset or below the floor is "unset" and falls back to the
    // default (so a degenerate tiny value never starves the feature); a ceiling, when given, clamps down.
    private static T ResolveWithFloor<T>(T? value, T floor, T fallback, T? ceiling = null)
        where T : struct, INumber<T> =>
        value is { } n && n >= floor
            ? T.Min(n, ceiling ?? n)
            : fallback;
}
