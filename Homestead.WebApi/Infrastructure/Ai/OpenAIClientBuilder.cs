using System.ClientModel;
using OpenAI;

namespace Homestead.WebApi.Infrastructure.Ai;

// Builds the raw OpenAIClient used by the chat-client factory. The OpenAI-compatible path (Ollama,
// OpenWebUI) uses the override endpoint verbatim, or the SDK default when none is configured.
public static class OpenAIClientBuilder
{
    public static OpenAIClient Build(string apiKey, string? endpoint)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(endpoint))
            options.Endpoint = new Uri(endpoint);

        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }
}
