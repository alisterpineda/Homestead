using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction.Configurations;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines;

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
                    throw new BusinessRuleException("Invalid configuration JSON for the specified engine type.");
                ValidateTesseractConfig(tesseractConfig);
                break;
            case TextExtractionEngineType.AzureDocumentIntelligence:
                ValidateAzureDocumentIntelligenceConfig(configuration);
                break;
            default:
                throw new BusinessRuleException($"Unsupported engine type: {engineType}");
        }
    }

    private static void ValidateTesseractConfig(TesseractEngineConfig? config)
    {
        if (config is null)
            return;

        if (config.Languages.Count == 0)
            throw new BusinessRuleException("At least one language must be specified.");

        if (new HashSet<TesseractLanguage>(config.Languages).Count != config.Languages.Count)
            throw new BusinessRuleException("Duplicate languages are not allowed.");
    }

    private static void ValidateAzureDocumentIntelligenceConfig(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new BusinessRuleException("Configuration is required for Azure Document Intelligence engines.");

        if (!AzureDocumentIntelligenceEngineConfig.TryParse(configuration, out var config))
            throw new BusinessRuleException("Invalid configuration JSON for the specified engine type.");

        if (string.IsNullOrWhiteSpace(config!.Endpoint))
            throw new BusinessRuleException("Endpoint is required.");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new BusinessRuleException("API Key is required.");
    }
}
