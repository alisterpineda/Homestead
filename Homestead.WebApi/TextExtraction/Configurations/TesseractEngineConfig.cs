using System.Text.Json;
using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.TextExtraction.Configurations;

public record TesseractEngineConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public List<TesseractLanguage> Languages { get; init; } = [TesseractLanguage.Eng];

    public static bool TryParse(string? json, out TesseractEngineConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            config = JsonSerializer.Deserialize<TesseractEngineConfig>(json, JsonOptions);
            return config is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
