using System.ClientModel;
using Azure.AI.OpenAI;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.AiClassification.Configurations;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.AiClassification;

public class AiClientFactory : IAiClientFactory
{
    public IChatClient CreateClient(AiProvider provider)
    {
        return provider.ProviderType switch
        {
            AiProviderType.AzureAIFoundry => CreateAzureAIFoundryClient(provider),
            _ => throw new NotSupportedException($"AI provider type '{provider.ProviderType}' is not supported.")
        };
    }

    private static IChatClient CreateAzureAIFoundryClient(AiProvider provider)
    {
        if (!AzureAIFoundryProviderConfig.TryParse(provider.Configuration, out var config) || config is null)
            throw new InvalidOperationException($"Invalid configuration for AI provider '{provider.Name}'.");

        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new InvalidOperationException($"Endpoint is required for AI provider '{provider.Name}'.");
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException($"API key is required for AI provider '{provider.Name}'.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException($"Model is required for AI provider '{provider.Name}'.");

        var azureClient = new AzureOpenAIClient(
            new Uri(config.Endpoint),
            new ApiKeyCredential(config.ApiKey));

        return azureClient.GetChatClient(config.Model).AsIChatClient();
    }
}
