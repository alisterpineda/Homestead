using System.Text.Json;

namespace Homestead.WebApi.Infrastructure.AiClassification.Configurations;

public record AzureAIFoundryProviderConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }

    public static bool TryParse(string? json, out AzureAIFoundryProviderConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            config = JsonSerializer.Deserialize<AzureAIFoundryProviderConfig>(json, JsonOptions);
            return config is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
