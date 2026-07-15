namespace Homestead.WebApi.Modules.Documents.Services;

// Turns "which engine should this run use?" into a concrete engine key.
//
// This lives in Documents/Services rather than Infrastructure/TextExtraction because it is the seam
// between two different kinds of fact: an engine's DEFINITION is a deployment concern (config), but the
// household's CHOICE of engine is product data (the DocumentSettings row). Infrastructure stays
// settings-free — it knows how to run an engine, not which one anyone prefers.
public interface ITextExtractionEngineResolver
{
    // Resolves at ENQUEUE time, returning a key to stamp into a job payload.
    //
    // An explicit pick is validated here and rejected with a BusinessRuleException (400, synchronously,
    // before a job row exists) when it is unknown or unavailable. This is the one place the fall-back
    // rule does NOT apply: a user who picked azure-layout and silently got Tesseract instead would
    // receive precisely the output they rejected.
    //
    // With no explicit pick the household default is returned verbatim — including a dangling one. It is
    // deliberately not validated or substituted here: a default that has gone stale falls back at
    // extraction time, where the substitution can be recorded in provenance metadata instead of vanishing.
    Task<string> ResolveKeyAsync(string? requestedKey, CancellationToken ct);

    // The default path — uploads and statement parsing, which never carry an explicit pick.
    Task<string> ResolveDefaultKeyAsync(CancellationToken ct);
}
