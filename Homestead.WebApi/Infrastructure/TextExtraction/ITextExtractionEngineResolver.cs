namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Turns "which engine should this run use?" into a concrete engine key. Chain: explicit key ->
// Ocr:DefaultEngine -> the built-in "tesseract".
//
// Lives in Infrastructure as a singleton (like IAiProviderResolver): both an engine's DEFINITION and the
// CHOICE of default are deployment config, so there is no settings read and no DbContext. The Documents
// settings page only REPORTS the default; it cannot change it.
public interface ITextExtractionEngineResolver
{
    // What uploads and un-pinned reprocess runs use: Ocr:DefaultEngine when set, else "tesseract".
    // Returned verbatim — including a dangling one. It is deliberately not validated or substituted here:
    // a default that names no engine falls back at extraction time, where TextExtractionHelper records
    // the substitution in provenance metadata instead of letting it vanish.
    string DefaultKey { get; }

    // Resolves at ENQUEUE time, returning a key to stamp into a job payload.
    //
    // An explicit pick is validated here and rejected with a BusinessRuleException (400, synchronously,
    // before a job row exists) when it is unknown or unavailable. This is the one place the fall-back
    // rule does NOT apply: a user who picked azure-layout and silently got Tesseract instead would
    // receive precisely the output they rejected. With no explicit pick, DefaultKey is returned.
    string ResolveKey(string? requestedKey);
}
