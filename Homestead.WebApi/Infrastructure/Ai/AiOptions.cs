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

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Model);
}
