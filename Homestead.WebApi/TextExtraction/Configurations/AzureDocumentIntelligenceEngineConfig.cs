using System.Text.Json;

namespace Homestead.WebApi.TextExtraction.Configurations;

public record AzureDocumentIntelligenceEngineConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }

    public static bool TryParse(string? json, out AzureDocumentIntelligenceEngineConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            config = JsonSerializer.Deserialize<AzureDocumentIntelligenceEngineConfig>(json, JsonOptions);
            return config is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
