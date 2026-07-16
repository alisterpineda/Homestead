using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

// One concrete class for every provider type (contrast the per-type OCR engines): the only per-type
// behavior is how the chat client is built, which a switch covers — everything downstream speaks
// IChatClient regardless.
public sealed class AiProvider : IAiProvider
{
    private readonly AiProviderOptions _options;
    private IChatClient? _client;

    public AiProvider(string key, AiProviderOptions options)
    {
        Key = key;
        _options = options;
        // Computed once at construction: config is static, so the verdict never changes.
        ConfigurationError = AiProviderConfiguration.Validate(key, options);
    }

    public string Key { get; }
    public AiProviderType? Type => _options.Type;
    public bool IsConfigured => ConfigurationError is null;
    public string? ConfigurationError { get; }

    public string? Endpoint => _options.Endpoint;
    public string? Model => _options.Model;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public int MaxInputChars => _options.ResolveMaxInputChars();
    public int StatementMaxInputChars => _options.ResolveStatementMaxInputChars();
    public bool ImageInput => _options.ImageInput;
    public int ImageRenderMaxEdge => _options.ResolveImageRenderMaxEdge();

    public IChatClient GetChatClient()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"AI provider '{Key}' is unavailable: {ConfigurationError}");

        // Built once and reused (singleton over static config): the underlying chat client is a stateless,
        // thread-safe HTTP wrapper. Rebuilding per call would spin up a fresh HTTP pipeline each time — a
        // new TLS handshake per classify against a remote endpoint and no connection reuse under bulk
        // classify. The benign init race (two concurrent first-calls) just discards a redundant, never-used
        // build. The client is provider-owned for the app lifetime, so callers must not dispose it.
        return _client ??= BuildClient();
    }

    private IChatClient BuildClient() => _options.Type switch
    {
        // The OpenAI-compatible path pointed at the configured endpoint. Local runtimes ignore the key, so
        // a dummy is substituted when none is configured; the endpoint is sent verbatim.
        AiProviderType.OpenAiCompatible => OpenAIClientBuilder
            .Build(string.IsNullOrWhiteSpace(_options.ApiKey) ? "ollama" : _options.ApiKey, _options.Endpoint)
            .GetChatClient(_options.Model)
            .AsIChatClient(),

        // AzureOpenAIClient subclasses OpenAIClient, so everything downstream (M.E.AI pipeline, strict
        // structured output, the 400 degradation ladder) is identical — it only changes auth (api-key
        // header) and api-version negotiation, which the plain client gets wrong against Azure.
        // Endpoint/ApiKey/Model are non-null here: Validate requires all three for this type, and the
        // IsConfigured guard runs before BuildClient.
        AiProviderType.AzureFoundry => new AzureOpenAIClient(
                new Uri(_options.Endpoint!), new AzureKeyCredential(_options.ApiKey!))
            .GetChatClient(_options.Model)
            .AsIChatClient(),

        // Unreachable behind the IsConfigured guard (a null/unknown Type always carries a ConfigurationError).
        _ => throw new InvalidOperationException($"AI provider '{Key}' has no usable Type.")
    };
}
