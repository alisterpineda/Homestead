using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.Ai;

public class AiClientFactory(IOptions<AiOptions> options) : IAiClientFactory
{
    private readonly AiOptions _options = options.Value;
    private IChatClient? _client;

    public bool IsConfigured => _options.IsConfigured;

    public IChatClient CreateClient()
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("AI is not configured (Ai:Endpoint and Ai:Model are required).");

        // Built once and reused (singleton over static config): the underlying OpenAI-compatible chat client
        // is a stateless, thread-safe HTTP wrapper. Rebuilding per call would spin up a fresh HTTP pipeline
        // each time — a new TLS handshake per classify against a remote endpoint and no connection reuse
        // under bulk classify. The benign init race (two concurrent first-calls) just discards a redundant,
        // never-used build. The client is factory-owned for the app lifetime, so callers must not dispose it.
        return _client ??= BuildClient();
    }

    private IChatClient BuildClient()
    {
        // Reuse the OpenAI-compatible path pointed at Ollama's /v1. Ollama ignores the key, so a dummy is
        // substituted when none is configured. normalizeForAzure: false sends the endpoint verbatim.
        var openAiClient = OpenAIClientBuilder.Build(
            string.IsNullOrWhiteSpace(_options.ApiKey) ? "ollama" : _options.ApiKey,
            _options.Endpoint,
            normalizeForAzure: false);

        return openAiClient.GetChatClient(_options.Model).AsIChatClient();
    }
}
