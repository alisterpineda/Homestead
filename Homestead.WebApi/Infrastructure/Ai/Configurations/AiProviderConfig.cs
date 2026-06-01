using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.Ai.Configurations;

// Unified config for all AI providers. The JSON keys (endpoint/apiKey/model) are deliberately
// kept identical across providers so existing stored rows need no data migration — only the C#
// type name and the UI labels differ (e.g. "Deployment name" for Azure vs "Default model" elsewhere).
public record AiProviderConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Default character budget for the document text embedded in the classification prompt, applied when
    // a provider does not configure MaxInputChars. The model is sent a head+tail sample of the extracted
    // text capped at this many characters.
    public const int DefaultMaxInputChars = 12_000;

    // Floor for a configured budget. A value below this (including a degenerate hand-seeded row that
    // bypasses the admin validator) is treated as "unset" and falls back to DefaultMaxInputChars, so a
    // tiny budget can never starve the prompt of document signal. Kept well above the elision marker so
    // the head+tail sampler always has room to work.
    public const int MinInputChars = 1_000;

    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }

    // Optional per-provider override for the document-text character budget — lets a large-context model
    // receive more of the document. Rides in the same Configuration JSON blob as the other keys, so adding
    // it needs no schema migration. Null (the common case) means "use DefaultMaxInputChars".
    public int? MaxInputChars { get; init; }

    // Resolves the effective character budget: the configured value when it clears the floor, else the
    // default. Centralised here so the classifier and the admin validator share one rule.
    public int ResolveMaxInputChars() =>
        MaxInputChars is int n && n >= MinInputChars ? n : DefaultMaxInputChars;

    public static bool TryParse(string? json, out AiProviderConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            config = JsonSerializer.Deserialize<AiProviderConfig>(json, JsonOptions);
            return config is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
