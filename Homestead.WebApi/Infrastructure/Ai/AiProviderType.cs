namespace Homestead.WebApi.Infrastructure.Ai;

// Lives here rather than Domain/Settings (contrast TextExtractionEngineType): the AI provider choice is
// config-only, never persisted — this enum only ever serializes into the admin status response.
public enum AiProviderType
{
    // An OpenAI-compatible chat endpoint (Ollama, OpenWebUI, OpenAI itself). Endpoint is the API base URL;
    // ApiKey is optional because local runtimes ignore it.
    OpenAiCompatible,

    // An Azure AI Foundry (Azure OpenAI) resource. Endpoint is the resource endpoint, Model is the
    // DEPLOYMENT name, and ApiKey is required — the Azure client sends it as the api-key header and
    // negotiates the api-version, which the plain OpenAI client gets wrong against Azure.
    AzureFoundry
}
