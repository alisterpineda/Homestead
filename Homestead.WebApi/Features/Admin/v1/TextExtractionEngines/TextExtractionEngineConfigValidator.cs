using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines;

public static class TextExtractionEngineConfigValidator
{
    public static void Validate(TextExtractionEngineType engineType, string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return;

        switch (engineType)
        {
            case TextExtractionEngineType.Tesseract:
                if (!TesseractEngineConfig.TryParse(configuration, out var config))
                    throw new BadRequestException("Invalid configuration JSON for the specified engine type.");
                ValidateTesseractConfig(config);
                break;
            default:
                throw new BadRequestException($"Unsupported engine type: {engineType}");
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
