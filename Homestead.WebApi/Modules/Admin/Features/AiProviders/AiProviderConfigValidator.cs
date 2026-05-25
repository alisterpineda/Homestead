using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.AiClassification.Configurations;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders;

public static class AiProviderConfigValidator
{
    public static void Validate(AiProviderType providerType, string? configuration)
    {
        switch (providerType)
        {
            case AiProviderType.AzureAIFoundry:
                ValidateAzureAIFoundryConfig(configuration);
                break;
            default:
                throw new BusinessRuleException($"Unsupported provider type: {providerType}");
        }
    }

    private static void ValidateAzureAIFoundryConfig(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new BusinessRuleException("Configuration is required for Azure AI Foundry providers.");

        if (!AzureAIFoundryProviderConfig.TryParse(configuration, out var config))
            throw new BusinessRuleException("Invalid configuration JSON for the specified provider type.");

        if (string.IsNullOrWhiteSpace(config!.Endpoint))
            throw new BusinessRuleException("Endpoint is required.");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new BusinessRuleException("API Key is required.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new BusinessRuleException("Deployment name is required.");
    }
}
