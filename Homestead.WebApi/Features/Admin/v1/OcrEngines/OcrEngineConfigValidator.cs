using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines;

public static class OcrEngineConfigValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static void Validate(OcrEngineType engineType, string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return;

        try
        {
            switch (engineType)
            {
                case OcrEngineType.Tesseract:
                    var config = JsonSerializer.Deserialize<TesseractEngineConfig>(configuration, JsonOptions);
                    ValidateTesseractConfig(config);
                    break;
                default:
                    throw new BadRequestException($"Unsupported engine type: {engineType}");
            }
        }
        catch (JsonException)
        {
            throw new BadRequestException("Invalid configuration JSON for the specified engine type.");
        }
    }

    private static void ValidateTesseractConfig(TesseractEngineConfig? config)
    {
        if (config is null)
            return;

        if (config.Languages.Count == 0)
            throw new BadRequestException("At least one language must be specified.");

        if (new HashSet<TesseractLanguage>(config.Languages).Count != config.Languages.Count)
            throw new BadRequestException("Duplicate languages are not allowed.");
    }
}
