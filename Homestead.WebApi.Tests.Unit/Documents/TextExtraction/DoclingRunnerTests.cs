using System.Net;
using System.Text;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The docling-serve wire contract (v1.38.2): submit-and-poll over the async API. A scripted handler
// stands in for the sidecar, in the FakeGoogleHttpHandler style, but routed by path because a
// conversion interleaves submit, polls, result, and /version.
public class DoclingRunnerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string Endpoint = "http://docling:5001/";

    private static DoclingConvertRequest Request(
        string? tableMode = null, bool forceOcr = false, TimeSpan? documentTimeout = null) =>
        new("3f2a9c.pdf", "application/pdf", tableMode, forceOcr, documentTimeout);

    private static DoclingRunner Runner(FakeDoclingHandler handler, string? apiKey = null, int maxConcurrency = 1) =>
        new(Endpoint, apiKey, new HttpClient(handler), NullLogger<DoclingRunner>.Instance, maxConcurrency)
        {
            PollGap = TimeSpan.Zero
        };

    private static Stream Document() => new MemoryStream("%PDF-1.7 fake"u8.ToArray());

    private static string TaskJson(string status) => $$"""{"task_id":"t-1","task_status":"{{status}}"}""";

    private static string Result(string status, string? md = "# Statement", string errors = "[]")
    {
        var mdJson = md is null ? "null" : $"\"{md}\"";
        return "{\"document\":{\"filename\":\"3f2a9c.pdf\",\"md_content\":" + mdJson + "},"
            + $"\"status\":\"{status}\",\"errors\":{errors},\"processing_time\":12.5,\"timings\":" + "{}}";
    }

    // The happy path: every leg of the protocol, in order, against the right URLs.
    [Fact]
    public async Task ConvertAsync_SubmitPollResult_ReturnsMarkdownAndFollowsTheProtocol()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("started"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("success"));
        handler.On("/version", HttpStatusCode.OK, """{"docling-serve":"1.38.2","docling":"2.60.0"}""");

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("# Statement", result.Markdown);
        Assert.Equal("success", result.Status);
        Assert.Empty(result.Errors);
        Assert.Equal(12.5, result.ProcessingSeconds);
        Assert.Equal("1.38.2", result.ServerVersion);
        Assert.Equal(
        [
            "POST http://docling:5001/v1/convert/file/async",
            "GET http://docling:5001/v1/status/poll/t-1?wait=2",
            "GET http://docling:5001/v1/status/poll/t-1?wait=2",
            "GET http://docling:5001/v1/status/poll/t-1?wait=2",
            "GET http://docling:5001/v1/result/t-1",
            "GET http://docling:5001/version",
        ], handler.Requests.Select(r => $"{r.Method} {r.Url}"));
    }

    [Fact]
    public async Task ConvertAsync_SubmitsTheFileAndFixedOptionsAsMultipart()
    {
        var handler = FakeDoclingHandler.Succeeding();

        await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        var submit = handler.Requests[0];
        var file = Assert.Single(submit.Parts, p => p.Name == "files");
        Assert.Equal("3f2a9c.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF-1.7 fake", file.Value);

        Assert.Equal("md", submit.Field("to_formats"));
        Assert.Equal("placeholder", submit.Field("image_export_mode"));
        Assert.Equal("<!-- PageBreak -->", submit.Field("md_page_break_placeholder"));
        Assert.Equal("false", submit.Field("force_ocr"));
        Assert.Equal("false", submit.Field("abort_on_error"));
        // Unset options are omitted, so the server's defaults apply.
        Assert.Null(submit.Field("table_mode"));
        Assert.Null(submit.Field("document_timeout"));
    }

    [Fact]
    public async Task ConvertAsync_ConfiguredOptions_AreSent()
    {
        var handler = FakeDoclingHandler.Succeeding();

        await Runner(handler).ConvertAsync(Document(),
            Request(tableMode: "fast", forceOcr: true, documentTimeout: TimeSpan.FromMinutes(19.5)), Ct);

        var submit = handler.Requests[0];
        Assert.Equal("fast", submit.Field("table_mode"));
        Assert.Equal("true", submit.Field("force_ocr"));
        Assert.Equal("1170", submit.Field("document_timeout"));
    }

    // document_timeout is whole seconds; a sub-second limit still sends 1, never 0 (no limit) or less.
    [Theory]
    [InlineData(0.4, "1")]
    [InlineData(10.9, "10")]
    public async Task ConvertAsync_DocumentTimeout_IsWholeSecondsAndAtLeastOne(double seconds, string expected)
    {
        var handler = FakeDoclingHandler.Succeeding();

        await Runner(handler).ConvertAsync(Document(),
            Request(documentTimeout: TimeSpan.FromSeconds(seconds)), Ct);

        Assert.Equal(expected, handler.Requests[0].Field("document_timeout"));
    }

    [Fact]
    public async Task ConvertAsync_ApiKeyConfigured_SendsItOnEveryRequest()
    {
        var handler = FakeDoclingHandler.Succeeding();

        await Runner(handler, apiKey: "s3cret").ConvertAsync(Document(), Request(), Ct);

        Assert.All(handler.Requests, r => Assert.Equal("s3cret", r.ApiKey));
    }

    [Fact]
    public async Task ConvertAsync_NoApiKey_SendsNoHeader()
    {
        var handler = FakeDoclingHandler.Succeeding();

        await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.All(handler.Requests, r => Assert.Null(r.ApiKey));
    }

    [Fact]
    public async Task ConvertAsync_TaskFails_ThrowsWithTheResultErrors()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("failure"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK,
            Result("failure", md: null, errors: """[{"component_type":"pipeline","error_message":"PDF is encrypted"}]"""));

        var ex = await Assert.ThrowsAsync<DoclingConversionException>(() =>
            Runner(handler).ConvertAsync(Document(), Request(), Ct));

        Assert.Equal("failure", ex.Status);
        Assert.Equal(["PDF is encrypted"], ex.Errors);
        Assert.Contains("PDF is encrypted", ex.Message);
    }

    // A failed task whose result cannot be read still fails as a conversion failure, not as an HTTP error.
    [Fact]
    public async Task ConvertAsync_TaskFailsAndResultIsGone_StillThrowsConversionFailure()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("failure"));
        handler.On("/v1/result/t-1", HttpStatusCode.NotFound, """{"detail":"Task not found."}""");

        var ex = await Assert.ThrowsAsync<DoclingConversionException>(() =>
            Runner(handler).ConvertAsync(Document(), Request(), Ct));

        Assert.Equal("failure", ex.Status);
        Assert.Empty(ex.Errors);
    }

    // A failed task's reasons sit on its final status and, as a TaskFailureResult, on the result endpoint
    // (v1.38.2's /openapi.json) — not in errors[]. All are kept, duplicates once.
    [Fact]
    public async Task ConvertAsync_TaskFails_ThrowsWithTheReasonsFromStatusAndTaskFailureResult()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK,
            """{"task_id":"t-1","task_status":"failure","error_message":"Worker died","failure":{"category":"backend_failure","message":"Out of memory","retryable":false,"phase":"conversion"}}""");
        handler.On("/v1/result/t-1", HttpStatusCode.OK,
            """{"kind":"TaskFailureResult","failure":{"category":"backend_failure","message":"Out of memory","retryable":false,"phase":"conversion"}}""");

        var ex = await Assert.ThrowsAsync<DoclingConversionException>(() =>
            Runner(handler).ConvertAsync(Document(), Request(), Ct));

        Assert.Equal("failure", ex.Status);
        Assert.Equal(["Worker died", "Out of memory"], ex.Errors);
    }

    // task_status shares the ConversionStatus enum. v1.38.2 reports a skipped or partial document as task
    // "success", but should a settled task ever say so directly, the runner must fetch the result rather
    // than poll until the deadline.
    [Theory]
    [InlineData("partial_success")]
    [InlineData("skipped")]
    [InlineData("something_new")]
    public async Task ConvertAsync_TaskSettlesWithAnotherStatus_FetchesTheResult(string taskStatus)
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("started"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson(taskStatus));
        // Never reached when the rule holds; a runner that kept polling stops here (third poll) and fails
        // the count below instead of spinning forever on the fake's repeated last answer.
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("partial_success"));
        handler.On("/version", HttpStatusCode.OK, """{"docling-serve":"1.38.2"}""");

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("partial_success", result.Status);
        Assert.Equal(2, handler.Requests.Count(r => r.Url.Contains("/v1/status/poll/")));
    }

    // A poll answering without task_status is not a settled task: keep polling (the deadline bounds it).
    [Fact]
    public async Task ConvertAsync_PollWithoutStatus_KeepsPolling()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, """{"task_id":"t-1"}""");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("success"));
        handler.On("/version", HttpStatusCode.OK, """{"docling-serve":"1.38.2"}""");

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("success", result.Status);
        Assert.Equal(2, handler.Requests.Count(r => r.Url.Contains("/v1/status/poll/")));
    }

    // A held conversion: its first poll blocks until Release(), so the runner's slot stays taken.
    private sealed class HeldConversion
    {
        private readonly ManualResetEventSlim _gate = new();

        public TaskCompletionSource Submitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeDoclingHandler Handler { get; } = new();

        public HeldConversion()
        {
            Handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"), () => Submitted.TrySetResult());
            Handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"), () => _gate.Wait());
            Handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
            Handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("success"));
            Handler.On("/version", HttpStatusCode.OK, """{"docling-serve":"1.38.2"}""");
        }

        public void Release() => _gate.Set();
    }

    private static int Submits(FakeDoclingHandler handler) =>
        handler.Requests.Count(r => r.Method == "POST");

    // The cap: a second conversion is not submitted while the only slot is taken, and goes once it frees.
    [Fact]
    public async Task ConvertAsync_SlotTaken_WaitsAndSubmitsAfterTheFirstFinishes()
    {
        var held = new HeldConversion();
        var runner = Runner(held.Handler, maxConcurrency: 1);

        var first = Task.Run(() => runner.ConvertAsync(Document(), Request(), Ct), Ct);
        await held.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = Task.Run(() => runner.ConvertAsync(Document(), Request(), Ct), Ct);

        await Task.Delay(200, Ct);
        Assert.Equal(1, Submits(held.Handler));

        held.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(2, Submits(held.Handler));
        var urls = held.Handler.Requests.Select(r => $"{r.Method} {r.Url}").ToList();
        Assert.True(
            urls.LastIndexOf("POST http://docling:5001/v1/convert/file/async")
                > urls.IndexOf("GET http://docling:5001/v1/result/t-1"),
            "the second conversion was submitted before the first one's result was read");
    }

    // Two slots: both conversions are in flight at once.
    [Fact]
    public async Task ConvertAsync_TwoSlots_SubmitsBothAtOnce()
    {
        var held = new HeldConversion();
        var runner = Runner(held.Handler, maxConcurrency: 2);

        var first = Task.Run(() => runner.ConvertAsync(Document(), Request(), Ct), Ct);
        await held.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = Task.Run(() => runner.ConvertAsync(Document(), Request(), Ct), Ct);

        // Both submitted while the first is still held, i.e. before either result was read.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Submits(held.Handler) < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(20, Ct);
        var submittedWhileHeld = Submits(held.Handler);

        held.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(2, submittedWhileHeld);
    }

    // The deadline runs out while waiting for a slot: the wait is cancelled and nothing is submitted, so
    // no task is left in the sidecar to convert after its job gave up.
    [Fact]
    public async Task ConvertAsync_DeadlinePassesWhileWaitingForASlot_CancelsWithoutSubmitting()
    {
        var held = new HeldConversion();
        var runner = Runner(held.Handler, maxConcurrency: 1);

        var first = Task.Run(() => runner.ConvertAsync(Document(), Request(), Ct), Ct);
        await held.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.ConvertAsync(Document(), Request(), deadline.Token));
        Assert.Equal(1, Submits(held.Handler));

        held.Release();
        await first.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    // A conversion that got its slot late is sent what is left of its limit, not the whole of it, so the
    // sidecar stops near the caller's deadline rather than a wait's length after it.
    [Fact]
    public async Task ConvertAsync_SlotTakenForAWhile_ShortensDocumentTimeoutByTheWait()
    {
        var held = new HeldConversion();
        var runner = Runner(held.Handler, maxConcurrency: 1);

        var first = Task.Run(() => runner.ConvertAsync(Document(), Request(), Ct), Ct);
        await held.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = Task.Run(() =>
            runner.ConvertAsync(Document(), Request(documentTimeout: TimeSpan.FromSeconds(60)), Ct), Ct);

        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        held.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var sent = int.Parse(held.Handler.Requests.Last(r => r.Method == "POST").Field("document_timeout")!);
        Assert.InRange(sent, 1, 58);
    }

    // partial_success is the engine's call (keep the text, warn) — the runner just reports it.
    [Fact]
    public async Task ConvertAsync_PartialSuccess_ReturnsTextAndErrors()
    {
        var handler = FakeDoclingHandler.Succeeding(Result("partial_success",
            errors: """[{"error_message":"Page 3 timed out"},{"error_message":""},{"component_type":"x"}]"""));

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("partial_success", result.Status);
        Assert.Equal("# Statement", result.Markdown);
        Assert.Equal(["Page 3 timed out"], result.Errors);
    }

    [Fact]
    public async Task ConvertAsync_SubmitUnauthorized_ThrowsHttpRequestExceptionWithTheStatus()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.Unauthorized, """{"detail":"Invalid API key"}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Runner(handler, apiKey: "wrong").ConvertAsync(Document(), Request(), Ct));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ConvertAsync_SubmitReturnsNoTaskId_Throws()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, """{"task_status":"pending"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner(handler).ConvertAsync(Document(), Request(), Ct));

        Assert.Contains("task_id", ex.Message);
    }

    // The sidecar may be restarting: a couple of failed polls are ridden out.
    [Fact]
    public async Task ConvertAsync_TwoPollFailuresThenSuccess_Completes()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.ServiceUnavailable, "");
        handler.OnThrow("/v1/status/poll/t-1", new HttpRequestException("connection refused"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("success"));

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("success", result.Status);
    }

    // The limit is on failures IN A ROW: a good poll in between starts the count over, so a long
    // conversion survives any number of separate blips.
    [Fact]
    public async Task ConvertAsync_PollFailuresSeparatedByASuccessfulPoll_Completes()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.ServiceUnavailable, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.ServiceUnavailable, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("started"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.ServiceUnavailable, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.ServiceUnavailable, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("success"));

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("success", result.Status);
    }

    [Fact]
    public async Task ConvertAsync_ThreeConsecutivePollFailures_Throws()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("started"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.NotFound, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.NotFound, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.NotFound, "");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Runner(handler).ConvertAsync(Document(), Request(), Ct));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal(1 + 4, handler.Requests.Count);
    }

    // The runner never translates cancellation: the helper tells its own deadline from the job's token.
    [Fact]
    public async Task ConvertAsync_CallerCancelsMidPoll_PropagatesCancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("started"), onRequest: cts.Cancel);
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Runner(handler).ConvertAsync(Document(), Request(), cts.Token));

        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("/v1/result/"));
    }

    [Fact]
    public async Task ConvertAsync_VersionForbidden_LeavesServerVersionNull()
    {
        var handler = FakeDoclingHandler.Succeeding(versionStatus: HttpStatusCode.Forbidden);

        var result = await Runner(handler).ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("success", result.Status);
        Assert.Null(result.ServerVersion);
    }

    [Fact]
    public async Task ConvertAsync_Version_IsReadOncePerRunner()
    {
        // Each scripted response repeats, so two conversions run against the one script.
        var handler = FakeDoclingHandler.Succeeding();
        var runner = Runner(handler);

        var first = await runner.ConvertAsync(Document(), Request(), Ct);
        var second = await runner.ConvertAsync(Document(), Request(), Ct);

        Assert.Equal("1.38.2", first.ServerVersion);
        Assert.Equal("1.38.2", second.ServerVersion);
        Assert.Single(handler.Requests, r => r.Url.EndsWith("/version"));
    }

    // No answer at all is not an answer: the next conversion asks again, and a success never fails over it.
    [Fact]
    public async Task ConvertAsync_VersionUnreachable_StillSucceedsAndAsksAgainNextTime()
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, TaskJson("pending"));
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, TaskJson("success"));
        handler.On("/v1/result/t-1", HttpStatusCode.OK, Result("success"));
        handler.OnThrow("/version", new HttpRequestException("connection reset"));
        handler.On("/version", HttpStatusCode.OK, """{"docling-serve":"1.38.2"}""");
        var runner = Runner(handler);

        var first = await runner.ConvertAsync(Document(), Request(), Ct);
        var second = await runner.ConvertAsync(Document(), Request(), Ct);

        Assert.Null(first.ServerVersion);
        Assert.Equal("1.38.2", second.ServerVersion);
    }

    [Fact]
    public async Task ConvertAsync_VersionForbidden_IsNotAskedAgain()
    {
        var handler = FakeDoclingHandler.Succeeding(versionStatus: HttpStatusCode.Forbidden);
        var runner = Runner(handler);

        await runner.ConvertAsync(Document(), Request(), Ct);
        await runner.ConvertAsync(Document(), Request(), Ct);

        Assert.Single(handler.Requests, r => r.Url.EndsWith("/version"));
    }

    // --- The tolerant readers ---

    [Fact]
    public void ParseResult_MissingKeys_ReadsAsEmptyRatherThanThrowing()
    {
        var result = DoclingRunner.ParseResult("{}");

        Assert.Null(result.Markdown);
        Assert.Equal("unknown", result.Status);
        Assert.Empty(result.Errors);
        Assert.Null(result.ProcessingSeconds);
    }

    // The two result shapes v1.38.2 declares: a document's errors[], and a failed task's failure.message.
    [Fact]
    public void ParseResult_TaskFailureResult_ReadsTheFailureMessage()
    {
        var result = DoclingRunner.ParseResult(
            """{"kind":"TaskFailureResult","failure":{"category":"timeout","message":"Task expired","retryable":true,"phase":"conversion"}}""");

        Assert.Null(result.Markdown);
        Assert.Equal(["Task expired"], result.Errors);
    }

    [Theory]
    [InlineData("""{"docling-serve":"1.38.2"}""", "1.38.2")]
    [InlineData("""{"docling":"2.60.0"}""", null)]
    [InlineData("""{"docling-serve":1}""", null)]
    [InlineData("""[]""", null)]
    public void ParseServerVersion_ReadsTheServersOwnEntry(string json, string? expected) =>
        Assert.Equal(expected, DoclingRunner.ParseServerVersion(json));

    [Fact]
    public void ParseTask_ReadsIdAndStatus()
    {
        var (id, status) = DoclingRunner.ParseTask("""{"task_id":"abc","task_status":"started","task_position":2}""");

        Assert.Equal("abc", id);
        Assert.Equal("started", status);
    }
}

/// <summary>
/// A scripted docling-serve: responses queued per path (the query string ignored), consumed in order, the
/// last one for a path repeating once its queue is down to one. Records each request as it arrives,
/// multipart parts included, because the runner disposes its content after sending.
/// </summary>
public class FakeDoclingHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Responder>> _routes = new();

    public List<RecordedRequest> Requests { get; } = [];

    private sealed record Responder(HttpStatusCode Status, string Body, Exception? Throw, Action? OnRequest);

    public sealed record RecordedPart(string? Name, string? FileName, string? ContentType, string Value);

    public sealed record RecordedRequest(string Method, string Url, string? ApiKey, IReadOnlyList<RecordedPart> Parts)
    {
        public string? Field(string name) => Parts.SingleOrDefault(p => p.Name == name)?.Value;
    }

    // Every leg succeeds at once; the submit is answered "pending" and the first poll "success".
    public static FakeDoclingHandler Succeeding(
        string? result = null, HttpStatusCode versionStatus = HttpStatusCode.OK)
    {
        var handler = new FakeDoclingHandler();
        handler.On("/v1/convert/file/async", HttpStatusCode.OK, """{"task_id":"t-1","task_status":"pending"}""");
        handler.On("/v1/status/poll/t-1", HttpStatusCode.OK, """{"task_id":"t-1","task_status":"success"}""");
        handler.On("/v1/result/t-1", HttpStatusCode.OK, result
            ?? """{"document":{"md_content":"# Statement"},"status":"success","errors":[],"processing_time":12.5}""");
        handler.On("/version", versionStatus,
            versionStatus == HttpStatusCode.OK ? """{"docling-serve":"1.38.2"}""" : """{"detail":"Forbidden"}""");
        return handler;
    }

    public void On(string path, HttpStatusCode status, string body, Action? onRequest = null) =>
        Route(path).Enqueue(new Responder(status, body, null, onRequest));

    public void OnThrow(string path, Exception exception) =>
        Route(path).Enqueue(new Responder(HttpStatusCode.OK, "", exception, null));

    private Queue<Responder> Route(string path)
    {
        if (!_routes.TryGetValue(path, out var queue))
            _routes[path] = queue = new Queue<Responder>();
        return queue;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var parts = new List<RecordedPart>();
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                parts.Add(new RecordedPart(
                    part.Headers.ContentDisposition?.Name?.Trim('"'),
                    part.Headers.ContentDisposition?.FileName?.Trim('"'),
                    part.Headers.ContentType?.MediaType,
                    await part.ReadAsStringAsync(cancellationToken)));
            }
        }

        Requests.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.ToString(),
            request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null,
            parts));

        if (!_routes.TryGetValue(request.RequestUri.AbsolutePath, out var queue) || queue.Count == 0)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };

        var responder = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        responder.OnRequest?.Invoke();

        if (responder.Throw is { } failure)
            throw failure;

        return new HttpResponseMessage(responder.Status)
        {
            Content = new StringContent(responder.Body, Encoding.UTF8, "application/json"),
        };
    }
}
