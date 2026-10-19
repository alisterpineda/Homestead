using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic.Core;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.Logging;

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

    private readonly CapturingLogger _logger = new();

    private AnthropicAiProvider Provider(string? endpoint = null, bool pdfUpload = true, TimeSpan? deleteTimeout = null) =>
        new(new AiOptions { ApiKey = "sk-test", Model = _model, Endpoint = endpoint, PdfUpload = pdfUpload }, false,
            _logger, _telemetry, new HttpClient(_handler))
        {
            DeleteTimeout = deleteTimeout ?? TimeSpan.FromSeconds(10),
        };

    private static AiRequest Request(bool longOutput = false, params AiPdfPart[] pdfs) =>
        new(AiFeature.Classification, AiEffort.Medium, "system", [.. pdfs, new AiTextPart(Prompt)], Schema,
            longOutput, "Test request");

    private Task<AiReply> Send(AnthropicAiProvider provider, bool longOutput = false) =>
        provider.SendAsync(Request(longOutput), TestContext.Current.CancellationToken);

    private static readonly AiPdfPart Pdf = new("%PDF-SENTINEL-ONE"u8.ToArray());
    private static readonly AiPdfPart Pdf2 = new("%PDF-SENTINEL-TWO"u8.ToArray());

    private Task<AiReply> SendPdf(AnthropicAiProvider provider, bool longOutput = false, CancellationToken? ct = null,
        params AiPdfPart[] pdfs) =>
        provider.SendAsync(Request(longOutput, pdfs.Length == 0 ? [Pdf] : pdfs),
            ct ?? TestContext.Current.CancellationToken);


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
        _handler.RespondStream(StreamedReply());

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

    // ---- PDFs through the Files API -----------------------------------------------------------------------------

    [Fact]
    public async Task Pdf_IsUploaded_ReferencedByFileId_ThenDeleted()
    {
        _handler.Respond(Message());

        var reply = await SendPdf(Provider());

        Assert.Equal("""{"ok":true}""", reply.Json);
        Assert.Equal(
            ["POST /v1/files", "POST /v1/messages?beta=true", "DELETE /v1/files/file_1"],
            _handler.Requests.Select(r => r.Route));

        // The upload: the PDF bytes, a neutral filename (the Console lists it), the one-hour expiry backstop, and
        // no files beta header (the Files API is GA; the beta shapes differ).
        var upload = _handler.Requests[0];
        var form = upload.Body.Replace("\"", "");
        Assert.Contains("%PDF-SENTINEL-ONE", form);
        Assert.Contains($"filename={AnthropicAiProvider.UploadFileName}", form);
        Assert.Contains("name=expires_in_seconds", form);
        Assert.Contains($"\r\n\r\n{AnthropicAiProvider.UploadExpirySeconds}\r\n", form);
        Assert.Contains("Content-Type: application/pdf", form);
        Assert.DoesNotContain("files-api", upload.Beta);

        // The messages call references the upload and carries no bytes.
        var messages = _handler.Requests[1];
        Assert.DoesNotContain("base64", messages.Body);
        using var body = JsonDocument.Parse(messages.Body);
        var source = body.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("source");
        Assert.Equal("file", source.GetProperty("type").GetString());
        Assert.Equal("file_1", source.GetProperty("file_id").GetString());
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public async Task Pdf_Streamed_IsDeletedAfterTheStreamIsAssembled()
    {
        _handler.RespondStream(StreamedReply());

        var reply = await SendPdf(Provider(), longOutput: true);

        Assert.Equal("""{"ok":true}""", reply.Json);
        Assert.Equal(
            ["POST /v1/files", "POST /v1/messages?beta=true", "DELETE /v1/files/file_1"],
            _handler.Requests.Select(r => r.Route));
    }

    [Fact]
    public async Task Pdfs_AreUploadedInPartOrder_AndAllDeleted()
    {
        _handler.Respond(Message());

        await SendPdf(Provider(), pdfs: [Pdf, Pdf2]);

        Assert.Equal(
            ["POST /v1/files", "POST /v1/files", "POST /v1/messages?beta=true",
                "DELETE /v1/files/file_1", "DELETE /v1/files/file_2"],
            _handler.Requests.Select(r => r.Route));
        Assert.Contains("SENTINEL-ONE", _handler.Requests[0].Body);
        Assert.Contains("SENTINEL-TWO", _handler.Requests[1].Body);
    }

    [Fact]
    public async Task Pdf_IsDeleted_WhenTheMessagesCallFails()
    {
        _handler.Respond("""{"type":"error","error":{"type":"invalid_request_error","message":"bad"}}""",
            HttpStatusCode.BadRequest);
        using var meter = new MeterCapture(_model);

        await Assert.ThrowsAsync<AnthropicBadRequestException>(() => SendPdf(Provider()));

        Assert.Equal("DELETE /v1/files/file_1", _handler.Requests[^1].Route);
        Assert.Equal(["error"], meter.Outcomes);
    }

    [Fact]
    public async Task Pdf_IsDeleted_WhenTheReplyIsCutOff()
    {
        _handler.Respond(Message("max_tokens"));

        await Assert.ThrowsAsync<AiResponseIncompleteException>(() => SendPdf(Provider()));

        Assert.Equal("DELETE /v1/files/file_1", _handler.Requests[^1].Route);
    }

    // The caller's cancellation must not cancel the cleanup.
    [Fact]
    public async Task Pdf_IsDeleted_WhenTheCallerCancels()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _handler.OnMessages = () => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendPdf(Provider(), ct: cts.Token));

        Assert.Equal("DELETE /v1/files/file_1", _handler.Requests[^1].Route);
    }

    // The reply already succeeded and the upload expires on its own: a failed delete is a warning, not an error.
    [Fact]
    public async Task FailedDelete_IsLogged_AndTheReplyStillReturned()
    {
        _handler.Respond(Message());
        _handler.DeleteStatus = HttpStatusCode.NotFound;

        var reply = await SendPdf(Provider());

        Assert.Equal("""{"ok":true}""", reply.Json);
        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains("file_1", warning);
    }

    // One failed delete doesn't stop the next: each is tried on its own.
    [Fact]
    public async Task FailedDeletes_AreEachTried_AndEachLogged()
    {
        _handler.Respond(Message());
        _handler.DeleteStatus = HttpStatusCode.NotFound;

        await SendPdf(Provider(), pdfs: [Pdf, Pdf2]);

        Assert.Equal(["DELETE /v1/files/file_1", "DELETE /v1/files/file_2"],
            _handler.Requests.Select(r => r.Route).Where(r => r.StartsWith("DELETE", StringComparison.Ordinal)));
        Assert.Equal(2, _logger.Warnings.Count);
    }

    // A delete that hangs is abandoned at the timeout: the reply still comes back, with a warning.
    [Fact]
    public async Task HangingDelete_IsAbandonedAtTheTimeout()
    {
        _handler.Respond(Message());
        _handler.DeleteHangs = true;

        var reply = await SendPdf(Provider(deleteTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Equal("""{"ok":true}""", reply.Json);
        Assert.Contains("file_1", Assert.Single(_logger.Warnings));
    }

    // No upload route at the endpoint (a proxy that only serves messages): a PDF rejection the caller can fall
    // back from, unlike the 404 a bad model ID gets from the messages call.
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task MissingUploadRoute_IsAPdfRejection(HttpStatusCode status)
    {
        _handler.UploadStatuses(status);
        using var meter = new MeterCapture(_model);

        var ex = await Assert.ThrowsAsync<AiPdfRejectedException>(() => SendPdf(Provider()));

        Assert.Equal((int)status, ex.Status);
        Assert.Contains("Ai:PdfUpload", ex.Message);
        Assert.Equal(["POST /v1/files"], _handler.Requests.Select(r => r.Route));
        Assert.Equal(["error"], meter.Outcomes);
    }

    // A rejected upload propagates with its status (so DocumentClassifier's PDF-rejection fallback sees a 400), is
    // metered as an error, and sends nothing else.
    [Fact]
    public async Task FailedUpload_Propagates_WithoutAMessagesCallOrDelete()
    {
        _handler.UploadStatuses(HttpStatusCode.BadRequest);
        using var meter = new MeterCapture(_model);

        var ex = await Assert.ThrowsAsync<AnthropicBadRequestException>(() => SendPdf(Provider()));

        Assert.True(AiProviderErrors.TryGetStatus(ex, out var status, out _));
        Assert.Equal(400, status);
        Assert.Equal(["POST /v1/files"], _handler.Requests.Select(r => r.Route));
        Assert.Equal(["error"], meter.Outcomes);
    }

    // The upload is built from the byte array, so the SDK's retry re-sends the whole file (a stream would be
    // spent by the first attempt and never retried).
    [Fact]
    public async Task UploadRetry_ResendsTheWholeFile()
    {
        _handler.Respond(Message());
        _handler.UploadStatuses(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        await SendPdf(Provider());

        Assert.Equal(
            ["POST /v1/files", "POST /v1/files", "POST /v1/messages?beta=true", "DELETE /v1/files/file_1"],
            _handler.Requests.Select(r => r.Route));
        Assert.Contains("%PDF-SENTINEL-ONE", _handler.Requests[1].Body);
    }

    // A second upload failing still deletes the first.
    [Fact]
    public async Task PartialUpload_DeletesWhatWasUploaded()
    {
        _handler.UploadStatuses(HttpStatusCode.OK, HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<AnthropicBadRequestException>(() => SendPdf(Provider(), pdfs: [Pdf, Pdf2]));

        Assert.Equal(["POST /v1/files", "POST /v1/files", "DELETE /v1/files/file_1"],
            _handler.Requests.Select(r => r.Route));
    }

    [Fact]
    public async Task PdfUploadOff_SendsThePdfInline_WithNoFileCalls()
    {
        _handler.Respond(Message());

        await SendPdf(Provider(pdfUpload: false));

        var request = Assert.Single(_handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        var source = body.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal(Convert.ToBase64String(Pdf.Bytes), source.GetProperty("data").GetString());
    }

    private string[] StreamedReply() =>
    [
        """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":""" + "\"" + _model + "\"" + ""","content":[],"stop_reason":null,"stop_sequence":null,"stop_details":null,"container":null,"context_management":null,"usage":{"input_tokens":50,"output_tokens":1}}}""",
        """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":"","citations":null}}""",
        """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"ok\":"}}""",
        """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"true}"}}""",
        """{"type":"content_block_stop","index":0}""",
        """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null,"stop_details":null,"container":null},"context_management":null,"usage":{"input_tokens":50,"output_tokens":9,"cache_read_input_tokens":null,"cache_creation_input_tokens":null,"server_tool_use":null}}""",
        """{"type":"message_stop"}""",
    ];

    public void Dispose() => _telemetry.Dispose();

    // Messages calls get the canned reply; the Files API routes answer on their own (uploads hand out file_1,
    // file_2, ...), so a PDF test only sets what it is about.
    private sealed class StubHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        private int _uploads;

        public List<(string Route, Uri Uri, string Body, string Beta)> Requests { get; } = [];

        private readonly Queue<HttpStatusCode> _uploadStatuses = [];

        // One status per upload attempt, in order; OK once they run out.
        public void UploadStatuses(params HttpStatusCode[] statuses)
        {
            foreach (var status in statuses)
                _uploadStatuses.Enqueue(status);
        }

        public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.OK;

        public bool DeleteHangs { get; set; }

        public Action? OnMessages { get; set; }

        public void Respond(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            _respond = () => Json(json, status);

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
            // A cancelled request never reaches the network, so it isn't recorded.
            ct.ThrowIfCancellationRequested();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var beta = request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "";
            var route = $"{request.Method} {request.RequestUri!.PathAndQuery}";
            lock (Requests) Requests.Add((route, request.RequestUri!, body, beta));

            if (request.RequestUri.AbsolutePath == "/v1/files")
            {
                var status = _uploadStatuses.TryDequeue(out var s) ? s : HttpStatusCode.OK;
                if (status != HttpStatusCode.OK)
                    return Error(status);
                var id = $"file_{Interlocked.Increment(ref _uploads)}";
                return Json($$"""
                    {"id":"{{id}}","type":"file","filename":"homestead-document.pdf","mime_type":"application/pdf",
                     "size_bytes":17,"created_at":"2026-10-19T00:00:00Z","downloadable":false,
                     "expires_at":"2026-10-19T01:00:00Z"}
                    """);
            }

            if (request.Method == HttpMethod.Delete && DeleteHangs)
                await Task.Delay(Timeout.Infinite, ct);

            if (request.Method == HttpMethod.Delete)
                return DeleteStatus == HttpStatusCode.OK
                    ? Json($$"""{"id":"{{request.RequestUri.Segments[^1]}}","type":"file_deleted"}""")
                    : Error(DeleteStatus);

            if (OnMessages is { } onMessages)
            {
                onMessages();
                ct.ThrowIfCancellationRequested();
            }
            return _respond();
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        // retry-after-ms keeps the SDK's retry of a 5xx from sleeping through its default backoff.
        private static HttpResponseMessage Error(HttpStatusCode status)
        {
            var response = Json("""{"type":"error","error":{"type":"api_error","message":"stub"}}""", status);
            response.Headers.Add("retry-after-ms", "1");
            return response;
        }
    }

    private sealed class CapturingLogger : ILogger<AnthropicAiProvider>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings { get { lock (_warnings) return _warnings.ToList(); } }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                lock (_warnings) _warnings.Add(formatter(state, exception));
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
