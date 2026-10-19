namespace Homestead.WebApi.Infrastructure.Ai;

// The consumer-side input policy: what the AI services put into a request (text budgets, whether to attach the
// page render and at what size, when to send the PDF itself) and the effort they ask for. Resolved — floored, defaulted, clamped — once at
// startup from AiOptions, and identical whichever backend runs, which is why it is not on IAiProvider. Unit tests
// pass a record literal.
public sealed record AiInputSettings(
    AiEffort Effort,
    int MaxInputChars,
    int StatementMaxInputChars,
    bool ImageInput,
    int ImageRenderMaxEdge,
    bool PdfInput,
    int PdfInputMaxPages,
    long PdfInputMaxBytes)
{
    // The production defaults (what an empty "Ai" section resolves to).
    public static AiInputSettings Default { get; } = From(new AiOptions());

    // An unparseable Effort falls back to medium here; the provider is unavailable in that case anyway (its
    // Availability carries the reason), so nothing is ever sent at the fallback.
    public static AiInputSettings From(AiOptions options) => new(
        AiConfiguration.ParseEffort(options.Effort) ?? AiEffort.Medium,
        options.ResolveMaxInputChars(),
        options.ResolveStatementMaxInputChars(),
        options.ImageInput,
        options.ResolveImageRenderMaxEdge(),
        options.PdfInput,
        options.ResolvePdfInputMaxPages(),
        options.ResolvePdfInputMaxBytes());
}
