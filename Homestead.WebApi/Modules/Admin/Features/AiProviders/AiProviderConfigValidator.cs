using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai.Configurations;

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
            case AiProviderType.OpenAI:
                ValidateOpenAIConfig(configuration);
                break;
            default:
                throw new BusinessRuleException($"Unsupported provider type: {providerType}");
        }
    }

    private static void ValidateAzureAIFoundryConfig(string? configuration)
    {
        var config = ParseOrThrow(configuration, "Azure AI Foundry");

        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new BusinessRuleException("Endpoint is required.");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new BusinessRuleException("API Key is required.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new BusinessRuleException("Deployment name is required.");
    }

    private static void ValidateOpenAIConfig(string? configuration)
    {
        var config = ParseOrThrow(configuration, "OpenAI");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new BusinessRuleException("API Key is required.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new BusinessRuleException("Default model is required.");

        // Endpoint is optional, but if supplied it must be a valid absolute http(s) URL — the SDK uses
        // it verbatim as the base address, so a relative URI or a non-http scheme fails opaquely later.
        if (!string.IsNullOrWhiteSpace(config.Endpoint)
            && (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpointUri)
                || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps)))
            throw new BusinessRuleException("Endpoint must be a valid absolute http(s) URL.");
    }

    private static AiProviderConfig ParseOrThrow(string? configuration, string providerLabel)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new BusinessRuleException($"Configuration is required for {providerLabel} providers.");

        if (!AiProviderConfig.TryParse(configuration, out var config) || config is null)
            throw new BusinessRuleException("Invalid configuration JSON for the specified provider type.");

        // Optional, but if supplied it must clear the floor — a smaller budget would be silently ignored by
        // the classifier (it falls back to the default), so reject it on write rather than mislead the admin.
        if (config.MaxInputChars is int max && max < AiProviderConfig.MinInputChars)
            throw new BusinessRuleException(
                $"Max input characters must be at least {AiProviderConfig.MinInputChars}.");

        return config;
    }
}
