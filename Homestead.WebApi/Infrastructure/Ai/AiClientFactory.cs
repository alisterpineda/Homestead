using System.ClientModel;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai.Configurations;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Homestead.WebApi.Infrastructure.Ai;

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

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(config.ApiKey),
            new OpenAIClientOptions { Endpoint = NormalizeEndpoint(config.Endpoint) });

        return openAiClient.GetChatClient(config.Model).AsIChatClient();
    }

    // /openai/v1 is versionless — the OpenAI client (unlike AzureOpenAIClient) won't append a dated api-version that Foundry rejects.
    private static Uri NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        if (!trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            trimmed += "/openai/v1";
        return new Uri(trimmed + "/");
    }
}
