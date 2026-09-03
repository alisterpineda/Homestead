namespace Homestead.WebApi.Infrastructure.Ai;

// Resolves which configured AI provider a call should use. Chain: explicit key -> Ai:DefaultProvider ->
// the sole defined provider -> null. A config-only singleton like ITextExtractionEngineResolver; the one
// difference is the terminus: OCR always has the built-in "tesseract" to land on, AI has nothing, hence
// the sole-provider convenience and the null result here.
public interface IAiProviderResolver
{
    // Null means UNRESOLVED — no provider is defined and none was asked for. That is "AI is not
    // configured": callers treat it as the graceful no-op (or a hard failure where AI is required).
    //
    // A key (explicit or Ai:DefaultProvider) naming a MISSING provider returns a synthetic unavailable
    // IAiProvider carrying the requested key and a "not defined" ConfigurationError — never null and never
    // a substitute provider, so a dangling key reads as unavailable-with-reason rather than silently
    // running something else. A key naming a MISCONFIGURED provider returns that provider (IsConfigured
    // false, reason attached).
    IAiProvider? Resolve(string? requestedKey = null);
}
