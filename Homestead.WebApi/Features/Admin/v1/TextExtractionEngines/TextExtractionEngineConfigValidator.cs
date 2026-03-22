using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Documents.TextExtraction.Configurations;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines;

public static class TextExtractionEngineConfigValidator
{
    public static void Validate(TextExtractionEngineType engineType, string? configuration)
    {
        switch (engineType)
        {
            case TextExtractionEngineType.Tesseract:
                if (string.IsNullOrWhiteSpace(configuration))
                    return;
                if (!TesseractEngineConfig.TryParse(configuration, out var tesseractConfig))
                    throw new BadRequestException("Invalid configuration JSON for the specified engine type.");
                ValidateTesseractConfig(tesseractConfig);
                break;
            case TextExtractionEngineType.AzureDocumentIntelligence:
                ValidateAzureDocumentIntelligenceConfig(configuration);
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

    private static void ValidateAzureDocumentIntelligenceConfig(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new BadRequestException("Configuration is required for Azure Document Intelligence engines.");

        if (!AzureDocumentIntelligenceEngineConfig.TryParse(configuration, out var config))
            throw new BadRequestException("Invalid configuration JSON for the specified engine type.");

        if (string.IsNullOrWhiteSpace(config!.Endpoint))
            throw new BadRequestException("Endpoint is required.");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new BadRequestException("API Key is required.");
    }
}
