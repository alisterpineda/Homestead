using System.Diagnostics;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Beta.Messages;

namespace Homestead.WebApi.Infrastructure.Ai;

// The one IAiProvider: the Claude API through the official Anthropic SDK, on the Beta messages endpoint (see
// AnthropicRequestMapper for why). Config is validated once, in the constructor, into Availability.
public sealed class AnthropicAiProvider : IAiProvider
{
    private readonly AiOptions _options;
    private readonly ILogger<AnthropicAiProvider> _logger;
    private readonly AiTelemetry _telemetry;
    private readonly HttpClient? _httpClient;
    private AnthropicClient? _client;

    public AnthropicAiProvider(
        AiOptions options, bool legacyKeysPresent, ILogger<AnthropicAiProvider> logger, AiTelemetry telemetry)
        : this(options, legacyKeysPresent, logger, telemetry, httpClient: null)
    {
    }

    // httpClient: a test seam, so SendAsync can be driven end to end against canned API replies.
    internal AnthropicAiProvider(
        AiOptions options, bool legacyKeysPresent, ILogger<AnthropicAiProvider> logger, AiTelemetry telemetry,
        HttpClient? httpClient)
    {
        _options = options;
        _logger = logger;
        _telemetry = telemetry;
        _httpClient = httpClient;
        // Computed once at construction: config is static, so the verdict never changes.
        Availability = AiConfiguration.Validate(options, legacyKeysPresent);
    }

    public AiAvailability Availability { get; }

    public async Task<AiReply> SendAsync(AiRequest request, CancellationToken ct)
    {
        Availability.EnsureConfigured();

        var parameters = AnthropicRequestMapper.ToParams(request, _options);
        var stream = AnthropicRequestMapper.ShouldStream(request);
        var configuredModel = _options.Model!;

        // Sizes only, never content: this is what diagnoses a 413 or a context-limit 400.
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("AI {Feature} request: {Parts}, stream {Stream}", AiTelemetry.FeatureTag(request.Feature),
                string.Join(", ", request.UserContent.Select(DescribeSize)), stream);

        var started = Stopwatch.GetTimestamp();
        BetaMessage message;
        try
        {
            // Aggregate assembles the streamed events into the same BetaMessage Create returns, and disposes the
            // stream on every exit path.
            message = stream
                ? await GetClient().Beta.Messages.CreateStreaming(parameters, ct).Aggregate()
                : await GetClient().Beta.Messages.Create(parameters, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _telemetry.RecordError(request.Feature, configuredModel, Stopwatch.GetElapsedTime(started));
            throw;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var reply = AnthropicReplyMapper.From(message);

        // Recorded before the completeness check: refused and cut-off replies are billed (a streamed cut-off is the
        // most expensive call there is), so they must show up in the meter.
        var outcome = !AnthropicReplyMapper.IsComplete(reply) ? AiTelemetry.Outcomes.Incomplete
            : reply.FallbackServed ? AiTelemetry.Outcomes.Fallback
            : AiTelemetry.Outcomes.Ok;
        _telemetry.RecordReply(request.Feature, reply.Model, reply.Usage, outcome, elapsed);
        _logger.LogInformation(
            "AI {Feature} call served by {Model} in {ElapsedMs} ms ({Outcome}): {InputTokens} input, {OutputTokens} output ({ThinkingTokens} thinking), {CacheReadTokens} cache-read, {CacheCreationTokens} cache-write tokens",
            AiTelemetry.FeatureTag(request.Feature), reply.Model, (long)elapsed.TotalMilliseconds, outcome,
            reply.Usage.InputTokens, reply.Usage.OutputTokens, reply.Usage.ThinkingTokens,
            reply.Usage.CacheReadInputTokens, reply.Usage.CacheCreationInputTokens);

        AnthropicReplyMapper.EnsureComplete(reply, request.RequestLabel);
        return new AiReply(reply.Json, reply.Model, reply.Usage);
    }

    private static string DescribeSize(AiContentPart part) => part switch
    {
        AiTextPart t => $"text {t.Text.Length} chars",
        AiImagePart i => $"image {i.MediaType} {i.Bytes.Length} bytes",
        AiPdfPart p => $"pdf {p.Bytes.Length} bytes",
        _ => part.GetType().Name,
    };

    // Built once and reused (singleton over static config): the SDK client is a stateless, thread-safe HTTP
    // wrapper, and rebuilding it per call would mean a new TLS handshake per classify and no connection reuse under
    // bulk classify. The benign init race (two concurrent first calls) just discards a redundant build.
    private AnthropicClient GetClient() => _client ??= new AnthropicClient(BuildClientOptions(_options, _httpClient));

    // Built from an explicit ClientOptions so config is the only source of where requests go and how they
    // authenticate. The SDK otherwise falls back to the process environment: ANTHROPIC_BASE_URL, an
    // ANTHROPIC_AUTH_TOKEN bearer header, an `ant auth login` profile (which can swap the base URL, or throw on a
    // missing ANTHROPIC_PROFILE), and ANTHROPIC_OPEN_TELEMETRY_* — all read inside the constructor, before an object
    // initializer's setters run, so every value must be in the options passed in, and each one set explicitly. The
    // one thing config cannot stop is ANTHROPIC_CUSTOM_HEADERS, which the SDK adds to every request.
    //
    // OpenTelemetry MetadataOnly is load-bearing: the SDK's spans would otherwise be able to carry the prompts and
    // replies (OCR text, the page image, statement lines) to the OTLP backend, and documents are personal. An
    // explicit value also stops the SDK reading the env var that could turn content on.
    internal static ClientOptions BuildClientOptions(AiOptions options, HttpClient? httpClient = null)
    {
        var clientOptions = new ClientOptions
        {
            ApiKey = options.ApiKey!,
            AuthToken = null,
            BaseUrl = string.IsNullOrWhiteSpace(options.Endpoint) ? EnvironmentUrl.Production : options.Endpoint,
            OpenTelemetry = new() { Traces = new() { ContentMode = OpenTelemetryContentMode.MetadataOnly } },
        };
        return httpClient is null ? clientOptions : clientOptions with { HttpClient = httpClient };
    }
}
