using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai.Configurations;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

public class AiClientFactory : IAiClientFactory
{
    public IChatClient CreateClient(AiProvider provider)
    {
        return provider.ProviderType switch
        {
            AiProviderType.AzureAIFoundry => CreateAzureAIFoundryClient(provider),
            AiProviderType.OpenAI => CreateOpenAIClient(provider),
            _ => throw new NotSupportedException($"AI provider type '{provider.ProviderType}' is not supported.")
        };
    }

    private static IChatClient CreateAzureAIFoundryClient(AiProvider provider)
    {
        var config = ParseConfig(provider);

        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new InvalidOperationException($"Endpoint is required for AI provider '{provider.Name}'.");
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException($"API key is required for AI provider '{provider.Name}'.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException($"Model is required for AI provider '{provider.Name}'.");

        var openAiClient = OpenAIClientBuilder.Build(config.ApiKey, config.Endpoint, normalizeForAzure: true);
        return openAiClient.GetChatClient(config.Model).AsIChatClient();
    }

    private static IChatClient CreateOpenAIClient(AiProvider provider)
    {
        var config = ParseConfig(provider);

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException($"API key is required for AI provider '{provider.Name}'.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException($"Model is required for AI provider '{provider.Name}'.");

        // Endpoint is optional for OpenAI — the SDK default is used when none is supplied.
        var openAiClient = OpenAIClientBuilder.Build(config.ApiKey, config.Endpoint, normalizeForAzure: false);
        return openAiClient.GetChatClient(config.Model).AsIChatClient();
    }

    private static AiProviderConfig ParseConfig(AiProvider provider)
    {
        if (!AiProviderConfig.TryParse(provider.Configuration, out var config) || config is null)
            throw new InvalidOperationException($"Invalid configuration for AI provider '{provider.Name}'.");
        return config;
    }
}
