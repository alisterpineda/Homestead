using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.Ai.Configurations;

// Unified config for all AI providers. The JSON keys (endpoint/apiKey/model) are deliberately
// kept identical across providers so existing stored rows need no data migration — only the C#
// type name and the UI labels differ (e.g. "Deployment name" for Azure vs "Default model" elsewhere).
public record AiProviderConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }

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
