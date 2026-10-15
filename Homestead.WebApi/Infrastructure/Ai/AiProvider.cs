using Anthropic;
using Anthropic.Core;
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
        // classify. What is cached is the whole built pipeline (the SDK client plus its middleware), so the
        // telemetry layer is shared too. The benign init race (two concurrent first-calls) just discards a
        // redundant, never-used build. The client is provider-owned for the app lifetime, so callers must not
        // dispose it — disposing the pipeline disposes the SDK client inside it.
        return _client ??= BuildClient();
    }

    // Every arm goes through the same M.E.AI pipeline (inner.AsBuilder().Use…().Build()). The first Use is the
    // outermost layer, so the Anthropic adapter wraps OpenTelemetry rather than the reverse: the spans then
    // record the options actually sent to the SDK (Temperature dropped, MaxOutputTokens resolved), not the
    // caller's. Build() without a service provider is enough: UseOpenTelemetry only resolves an optional
    // ILoggerFactory from it, which spans and metrics don't need.
    private IChatClient BuildClient()
    {
        var builder = BuildSdkClient().AsBuilder();

        // The Claude API takes temperature and max_tokens differently; the adapter makes callers unaware of it.
        if (_options.Type == AiProviderType.Anthropic)
            builder.Use(inner => new AnthropicChatOptionsClient(inner));

        // GenAI semantic-convention spans and metrics (model, token counts, finish reason, duration) under the
        // name ServiceDefaults subscribes to. EnableSensitiveData = false is an override, not a restatement of
        // the default: the library seeds it from OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT, which
        // Aspire sets to true for every resource in Development. Without it every prompt and response (OCR
        // text, the page image, extracted statement lines) would be exported to the OTLP backend, and
        // documents are personal — so this line must stay.
        return builder
            .UseOpenTelemetry(
                sourceName: Microsoft.Extensions.Hosting.Extensions.MicrosoftExtensionsAiTelemetryName,
                configure: otel => otel.EnableSensitiveData = false)
            .Build();
    }

    // The provider SDK's own IChatClient, before the shared pipeline wraps it.
    private IChatClient BuildSdkClient() => _options.Type switch
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

        // The Anthropic SDK's own IChatClient adapter maps the system prompt, the page-render DataContent, and
        // a ForJsonSchema response format (to output_config.format, forcing additionalProperties:false — the
        // schema is enforced, so the OpenAI-only "strict" flag is simply ignored). BuildClient wraps it in
        // AnthropicChatOptionsClient for the two options the Claude API treats differently.
        AiProviderType.Anthropic => CreateAnthropicClient().AsIChatClient(_options.Model),

        // Unreachable behind the IsConfigured guard (a null/unknown Type always carries a ConfigurationError).
        _ => throw new InvalidOperationException($"AI provider '{Key}' has no usable Type.")
    };

    // Built from an explicit ClientOptions so config is the only source of where requests go and how they
    // authenticate. The SDK otherwise falls back to the process environment: ANTHROPIC_BASE_URL, an
    // ANTHROPIC_AUTH_TOKEN bearer header, or an `ant auth login` profile (which can swap the base URL, or
    // throw on a missing ANTHROPIC_PROFILE). It resolves that profile inside the constructor, before an object
    // initializer's setters run — so every value must be in the options passed in, and each one set
    // explicitly: ApiKey (Validate requires it), AuthToken = null (no bearer header), and BaseUrl (the
    // Endpoint override, else the public API — never ANTHROPIC_BASE_URL). The one thing config cannot stop is
    // ANTHROPIC_CUSTOM_HEADERS, which the SDK adds to every request.
    private AnthropicClient CreateAnthropicClient() => new(new ClientOptions
    {
        ApiKey = _options.ApiKey!,
        AuthToken = null,
        BaseUrl = string.IsNullOrWhiteSpace(_options.Endpoint) ? EnvironmentUrl.Production : _options.Endpoint,
    });
}
