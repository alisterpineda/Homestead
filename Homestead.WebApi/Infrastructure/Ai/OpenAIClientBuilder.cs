using System.ClientModel;
using OpenAI;

namespace Homestead.WebApi.Infrastructure.Ai;

// Builds the raw OpenAIClient used in three places: the chat-client factory, the test-connection
// slice, and the list-models slice. Azure AI Foundry needs the endpoint normalized to the
// versionless /openai/v1 path; OpenAI uses any override endpoint verbatim (or the SDK default).
public static class OpenAIClientBuilder
{
    public static OpenAIClient Build(string apiKey, string? endpoint, bool normalizeForAzure)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(endpoint))
            options.Endpoint = normalizeForAzure ? NormalizeEndpoint(endpoint) : new Uri(endpoint);

        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
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
