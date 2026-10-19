using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic.Core;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// AnthropicAiProvider end to end against canned Claude API replies (a stub HttpMessageHandler stands in for the
// network): what reaches the wire, the completeness check, the metric outcome — and the client settings that keep
// document content out of telemetry and config the only source of where requests go.
public sealed class AnthropicAiProviderTests : IDisposable
{
    private const string Prompt = "PROMPT-SENTINEL classify this";
    private readonly string _model = "test-model-" + Guid.NewGuid().ToString("N");
    private readonly StubHandler _handler = new();
    private readonly AiTelemetry _telemetry = new();

    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { ok = new { type = "boolean" } }, required = new[] { "ok" },
        additionalProperties = false,
    });

    private AnthropicAiProvider Provider(string? endpoint = null) =>
        new(new AiOptions { ApiKey = "sk-test", Model = _model, Endpoint = endpoint }, false,
            NullLogger<AnthropicAiProvider>.Instance, _telemetry, new HttpClient(_handler));

    private static AiRequest Request(bool longOutput = false) =>
        new(AiFeature.Classification, AiEffort.Medium, "system", [new AiTextPart(Prompt)], Schema, longOutput,
            "Test request");

    private Task<AiReply> Send(AnthropicAiProvider provider, bool longOutput = false) =>
        provider.SendAsync(Request(longOutput), TestContext.Current.CancellationToken);

    private string Message(string stopReason = "end_turn", string? model = null, string usageExtra = "") => $$"""
        {
          "id": "msg_1", "type": "message", "role": "assistant", "model": "{{model ?? _model}}",
          "content": [{ "type": "text", "text": "{\"ok\":true}", "citations": null }],
          "stop_reason": "{{stopReason}}", "stop_sequence": null, "stop_details": null,
          "container": null, "context_management": null,
          "usage": { "input_tokens": 100, "output_tokens": 20 {{usageExtra}} }
        }
        """;

    // ---- Client settings ----------------------------------------------------------------------------------------

    [Fact]
    public void ClientOptions_PinMetadataOnlyTelemetry_NoBearerToken_AndTheProductionBaseUrl()
    {
        var options = AnthropicAiProvider.BuildClientOptions(new AiOptions { ApiKey = "sk-test", Model = "m" });

        Assert.Equal(OpenTelemetryContentMode.MetadataOnly, options.OpenTelemetry?.Traces?.ContentMode);
        Assert.Null(options.AuthToken);
        Assert.Equal("sk-test", options.ApiKey);
        Assert.Equal(EnvironmentUrl.Production, options.BaseUrl);
    }

    [Fact]
    public void ClientOptions_UseTheConfiguredEndpoint()
    {
        var options = AnthropicAiProvider.BuildClientOptions(
            new AiOptions { ApiKey = "sk-test", Model = "m", Endpoint = "https://proxy.example.com" });

        Assert.Equal("https://proxy.example.com", options.BaseUrl);
    }

    // The SDK reads ANTHROPIC_* at client construction; explicit options must win over every one of them, and the
    // span must carry no message content even when the environment asks for it.
    [Fact]
    public async Task Environment_CannotRedirectTheCall_OrPutContentInSpans()
    {
        string?[] previous =
        [
            Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL"),
            Environment.GetEnvironmentVariable("ANTHROPIC_OPEN_TELEMETRY_TRACES_CONTENT_MODE"),
        ];
        var tags = new List<KeyValuePair<string, object?>>();
        var events = new List<ActivityEvent>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name.StartsWith("com.anthropic.sdk.csharp", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.GetTagItem("gen_ai.request.model") as string == _model)
                {
                    lock (tags) { tags.AddRange(a.TagObjects); events.AddRange(a.Events); }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "https://attacker.example.com");
            Environment.SetEnvironmentVariable("ANTHROPIC_OPEN_TELEMETRY_TRACES_CONTENT_MODE", "content");
            _handler.Respond(Message());

            await Send(Provider());

            Assert.Equal("api.anthropic.com", Assert.Single(_handler.Requests).Uri.Host);
            Assert.NotEmpty(tags);
            Assert.DoesNotContain(tags, t => t.Value?.ToString()?.Contains("PROMPT-SENTINEL") == true);
            Assert.DoesNotContain(tags, t => t.Value?.ToString()?.Contains("\"ok\":true") == true);
            Assert.DoesNotContain(events, e => e.Tags.Any(t => t.Value?.ToString()?.Contains("PROMPT-SENTINEL") == true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", previous[0]);
            Environment.SetEnvironmentVariable("ANTHROPIC_OPEN_TELEMETRY_TRACES_CONTENT_MODE", previous[1]);
        }
    }

    // ---- SendAsync ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Buffered_PostsToTheBetaEndpoint_AndReturnsTheReply()
    {
        _handler.Respond(Message(usageExtra: """, "output_tokens_details": { "thinking_tokens": 7 }"""));
        using var meter = new MeterCapture(_model);

        var reply = await Send(Provider("https://proxy.example.com"));

        Assert.Equal("""{"ok":true}""", reply.Json);
        Assert.Equal(_model, reply.Model);
        Assert.Equal(new AiUsage(100, 20, 7, 0, 0), reply.Usage);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://proxy.example.com/v1/messages?beta=true", request.Uri.ToString());
        Assert.Contains("server-side-fallback-2026-07-01", request.Beta);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("medium", body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(body.RootElement.TryGetProperty("stream", out var s) && s.GetBoolean());

        Assert.Equal(["ok"], meter.Outcomes);
        Assert.Equal(100, meter.Tokens("input"));
        Assert.Equal(7, meter.Tokens("thinking"));
    }

    [Fact]
    public async Task LongOutput_Streams_AndAssemblesTheReply()
    {
        _handler.RespondStream(
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":""" + "\"" + _model + "\"" + ""","content":[],"stop_reason":null,"stop_sequence":null,"stop_details":null,"container":null,"context_management":null,"usage":{"input_tokens":50,"output_tokens":1}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":"","citations":null}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"ok\":"}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"true}"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null,"stop_details":null,"container":null},"context_management":null,"usage":{"input_tokens":50,"output_tokens":9,"cache_read_input_tokens":null,"cache_creation_input_tokens":null,"server_tool_use":null}}""",
            """{"type":"message_stop"}""");

        var reply = await Send(Provider(), longOutput: true);

        Assert.Equal("""{"ok":true}""", reply.Json);
        using var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(AnthropicRequestMapper.StreamedMaxTokens, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    // A cut-off reply is billed, so it is metered (outcome incomplete) before the completeness check throws.
    [Fact]
    public async Task CutOffReply_IsMeteredAsIncomplete_ThenThrows()
    {
        _handler.Respond(Message("max_tokens"));
        using var meter = new MeterCapture(_model);

        var ex = await Assert.ThrowsAsync<AiResponseIncompleteException>(() => Send(Provider()));

        Assert.Equal("Test request: the response hit the output-token limit and was cut off.", ex.Message);
        Assert.Equal(["incomplete"], meter.Outcomes);
        Assert.Equal(20, meter.Tokens("output"));
    }

    [Fact]
    public async Task FallbackServedReply_IsMeteredAsFallback()
    {
        _handler.Respond(Message(usageExtra: $$"""
            , "iterations": [
              { "type": "message", "input_tokens": 90, "output_tokens": 2, "cache_read_input_tokens": 0, "cache_creation_input_tokens": 0 },
              { "type": "fallback_message", "model": "{{_model}}", "input_tokens": 100, "output_tokens": 20, "cache_read_input_tokens": 0, "cache_creation_input_tokens": 0, "cache_creation": null }
            ]
            """));
        using var meter = new MeterCapture(_model);

        var reply = await Send(Provider());

        Assert.Equal(new AiUsage(190, 22, 0, 0, 0), reply.Usage);
        Assert.Equal(["fallback"], meter.Outcomes);
    }

    [Fact]
    public async Task ApiError_IsMeteredAsError_AndPropagates()
    {
        _handler.Respond("""{"type":"error","error":{"type":"invalid_request_error","message":"bad"}}""",
            HttpStatusCode.BadRequest);
        using var meter = new MeterCapture(_model);

        await Assert.ThrowsAsync<AnthropicBadRequestException>(() => Send(Provider()));

        Assert.Equal(["error"], meter.Outcomes);
        Assert.Single(_handler.Requests);
    }

    public void Dispose() => _telemetry.Dispose();

    private sealed class StubHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public List<(Uri Uri, string Body, string Beta)> Requests { get; } = [];

        public void Respond(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            _respond = () => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        public void RespondStream(params string[] events) =>
            _respond = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    string.Concat(events.Select(e =>
                        $"event: {JsonDocument.Parse(e).RootElement.GetProperty("type").GetString()}\ndata: {e}\n\n")),
                    Encoding.UTF8, "text/event-stream"),
            };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var beta = request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "";
            lock (Requests) Requests.Add((request.RequestUri!, body, beta));
            return _respond();
        }
    }

    // Captures this test's measurements only: the meter name is shared by every AiTelemetry in the process, so
    // measurements are filtered by the test's unique model tag.
    private sealed class MeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<string> _outcomes = [];
        private readonly Dictionary<string, long> _tokens = [];

        public MeterCapture(string model)
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AiTelemetry.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var tagList = tags.ToArray();
                if (!tagList.Any(t => t.Key == "model" && (string?)t.Value == model))
                    return;
                lock (_outcomes)
                {
                    if (instrument.Name == "homestead.ai.requests")
                        _outcomes.Add((string)tagList.Single(t => t.Key == "outcome").Value!);
                    else if (instrument.Name == "homestead.ai.tokens")
                    {
                        var kind = (string)tagList.Single(t => t.Key == "kind").Value!;
                        _tokens[kind] = _tokens.GetValueOrDefault(kind) + value;
                    }
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<string> Outcomes { get { lock (_outcomes) return _outcomes.ToList(); } }

        public long Tokens(string kind) { lock (_outcomes) return _tokens.GetValueOrDefault(kind); }

        public void Dispose() => _listener.Dispose();
    }
}
