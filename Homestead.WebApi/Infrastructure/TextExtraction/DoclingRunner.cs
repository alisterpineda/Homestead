using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// One runner per configured Docling engine instance, talking to a docling-serve sidecar over its async
// API: submit the file, poll the task until it settles, fetch the result.
//
// It OWNS its HttpClient (built by CreateHttpClient, one per engine instance, never disposed — the
// AzureDocumentIntelligenceRunner precedent of a singleton holding its own client). Deliberately not an
// IHttpClientFactory client: ServiceDefaults puts the standard resilience handler on every factory client
// (10 s per attempt, 30 s total, POSTs retried), which would re-submit a conversion as a duplicate task and
// cut off a multi-MB upload. With an infinite client timeout, the caller's token — TextExtractionHelper's
// deadline — is the one clock over the upload, the server-side queue wait, polling, and the result fetch.
//
// Cancellation is never translated here: the helper tells its own deadline from the job's token.
//
// At most maxConcurrency conversions are in flight at the sidecar (OcrEngineOptions.MaxConcurrency); the
// rest wait for a slot here, under the caller's token, so a job that runs out of time while waiting never
// submits a task docling-serve could not cancel.
public class DoclingRunner(
    string endpoint, string? apiKey, HttpClient http, ILogger<DoclingRunner> logger, int maxConcurrency = 1)
    : IDoclingRunner
{
    internal const string PageBreakPlaceholder = "<!-- PageBreak -->";

    // How long the server holds each status request open waiting for a change (its own long poll).
    internal const int PollWaitSeconds = 2;

    // Poll failures in a row tolerated before giving up: the sidecar may be restarting. A restart loses
    // the task, so its status then 404s, and three of those end the conversion rather than spinning on.
    internal const int MaxConsecutivePollFailures = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _baseUrl = endpoint.TrimEnd('/');

    // Never disposed, like the HttpClient: the runner lives as long as the app. Floored at 1 because an
    // invalid MaxConcurrency still builds a runner (the engine refuses before using it).
    private readonly SemaphoreSlim _slots = new(Math.Max(1, maxConcurrency));

    // Read once per runner, the first time a conversion finishes, and kept once the sidecar has answered
    // at all — a 403 (version info hidden) is an answer too, and it gives the same one every time. Only a
    // call that got no answer is retried on the next conversion.
    private string? _serverVersion;
    private bool _serverVersionRead;

    // The gap between status long-polls. Settable so tests need not wait it out.
    internal TimeSpan PollGap { get; init; } = TimeSpan.FromMilliseconds(500);

    public static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    public async Task<DoclingConvertResult> ConvertAsync(
        Stream document, DoclingConvertRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // A free slot is taken as is, leaving the limit untouched; only a real wait is measured.
        if (!_slots.Wait(0))
        {
            var queuedAt = Stopwatch.GetTimestamp();
            await _slots.WaitAsync(ct);
            var waited = Stopwatch.GetElapsedTime(queuedAt);
            logger.LogDebug("Waited {Waited} for a free conversion slot at docling-serve {Endpoint}",
                waited, _baseUrl);

            // The caller's deadline started before the wait; the sidecar's limit should end with it.
            request = request with { DocumentTimeout = request.DocumentTimeout - waited };
        }

        try
        {
            return await ConvertInSlotAsync(document, request, ct);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<DoclingConvertResult> ConvertInSlotAsync(
        Stream document, DoclingConvertRequest request, CancellationToken ct)
    {
        var taskId = await SubmitAsync(document, request, ct);
        var (status, pollBody) = await WaitForTaskAsync(taskId, ct);

        if (status == TaskStatuses.Failure)
        {
            // The task itself failed (not a document that converted badly — that is a task "success" whose
            // result says failure). The reason is on the final status (error_message, failure.message) and
            // on the result endpoint's TaskFailureResult; read both, but the failure stands either way.
            var errors = ParseTaskErrors(pollBody)
                .Concat(await TryReadFailureErrorsAsync(taskId, ct))
                .Distinct()
                .ToList();
            throw new DoclingConversionException(TaskStatuses.Failure, errors);
        }

        using var response = await SendAsync(HttpMethod.Get, $"/v1/result/{Uri.EscapeDataString(taskId)}", null, ct);
        response.EnsureSuccessStatusCode();
        var result = ParseResult(await response.Content.ReadAsStringAsync(ct));

        var version = await GetServerVersionAsync(ct);

        logger.LogInformation(
            "Docling task {TaskId} at {Endpoint} finished with status {Status} in {ProcessingSeconds}s",
            taskId, _baseUrl, result.Status, result.ProcessingSeconds);

        return result with { ServerVersion = version };
    }

    private async Task<string> SubmitAsync(Stream document, DoclingConvertRequest request, CancellationToken ct)
    {
        using var content = BuildSubmitContent(document, request);
        using var response = await SendAsync(HttpMethod.Post, "/v1/convert/file/async", content, ct);

        // A 401 (wrong key), 422 (a rejected option) or 5xx surfaces as an HttpRequestException carrying
        // the status. Whatever the status, TextExtractionHelper turns it into a failed extraction, which is
        // final: an upload keeps its document without text, a reprocess fails as text_extraction_failed.
        response.EnsureSuccessStatusCode();

        var task = ParseTask(await response.Content.ReadAsStringAsync(ct));
        if (string.IsNullOrWhiteSpace(task.TaskId))
            throw new InvalidOperationException($"docling-serve at {_baseUrl} accepted the file but returned no task_id.");

        logger.LogDebug("Submitted a conversion to docling-serve at {Endpoint} as task {TaskId}", _baseUrl, task.TaskId);
        return task.TaskId;
    }

    // Polls until the task leaves pending/started. v1.38.2 answers "success" even when the document itself
    // failed, was skipped, or converted partially (the result carries that), but task_status shares the
    // ConversionStatus enum, so any other settled value ends the wait too rather than spinning to the
    // deadline; the result is then fetched as for success. A poll with no task_status keeps polling.
    private async Task<(string Status, string Body)> WaitForTaskAsync(string taskId, CancellationToken ct)
    {
        var path = $"/v1/status/poll/{Uri.EscapeDataString(taskId)}?wait={PollWaitSeconds}";
        var consecutiveFailures = 0;

        while (true)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, path, null, ct);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(ct);
                var status = ParseTask(body).TaskStatus;
                consecutiveFailures = 0;

                if (status is not (null or TaskStatuses.Pending or TaskStatuses.Started))
                    return (status, body);
            }
            catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= MaxConsecutivePollFailures)
                    throw;

                logger.LogDebug(ex, "Polling Docling task {TaskId} failed ({Failures} in a row); retrying",
                    taskId, consecutiveFailures);
            }

            await Task.Delay(PollGap, ct);
        }
    }

    private async Task<IReadOnlyList<string>> TryReadFailureErrorsAsync(string taskId, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, $"/v1/result/{Uri.EscapeDataString(taskId)}", null, ct);
            return response.IsSuccessStatusCode
                ? ParseResult(await response.Content.ReadAsStringAsync(ct)).Errors
                : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogDebug(ex, "Could not read the result of failed Docling task {TaskId}", taskId);
            return [];
        }
    }

    private async Task<string?> GetServerVersionAsync(CancellationToken ct)
    {
        if (_serverVersionRead)
            return _serverVersion;

        try
        {
            // Auth-free, and 403 when the sidecar sets DOCLING_SERVE_SHOW_VERSION_INFO=false.
            using var response = await SendAsync(HttpMethod.Get, "/version", null, ct);
            _serverVersion = response.IsSuccessStatusCode
                ? ParseServerVersion(await response.Content.ReadAsStringAsync(ct))
                : null;
            _serverVersionRead = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Provenance garnish: never worth failing a conversion that already succeeded.
            logger.LogDebug(ex, "Could not read the docling-serve version at {Endpoint}", _baseUrl);
            _serverVersionRead = ex is JsonException;
            _serverVersion = null;
        }

        return _serverVersion;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var message = new HttpRequestMessage(method, _baseUrl + path) { Content = content };
        if (!string.IsNullOrEmpty(apiKey))
            message.Headers.Add("X-Api-Key", apiKey);
        return http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct);
    }

    internal static MultipartFormDataContent BuildSubmitContent(Stream document, DoclingConvertRequest request)
    {
        var file = new StreamContent(document);
        file.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);

        var content = new MultipartFormDataContent { { file, "files", request.FileName } };
        AddField(content, "to_formats", "md");
        // Pictures become a placeholder rather than inlined base64, which would bloat the stored text.
        AddField(content, "image_export_mode", "placeholder");
        // Keeps page boundaries findable in the Markdown, as Azure's page comments do.
        AddField(content, "md_page_break_placeholder", PageBreakPlaceholder);
        if (request.TableMode is { } tableMode)
            AddField(content, "table_mode", tableMode);
        AddField(content, "force_ocr", request.ForceOcr ? "true" : "false");
        // A page that fails should cost that page, not the document: the result says partial_success.
        AddField(content, "abort_on_error", "false");
        if (request.DocumentTimeout is { } timeout)
        {
            var seconds = Math.Max(1, (long)Math.Floor(timeout.TotalSeconds));
            AddField(content, "document_timeout", seconds.ToString(CultureInfo.InvariantCulture));
        }

        return content;
    }

    private static void AddField(MultipartFormDataContent content, string name, string value) =>
        content.Add(new StringContent(value), name);

    // The readers are tolerant by design: docling-serve's docs disagree on a few shapes, so a missing key
    // reads as null/empty rather than throwing, and the callers decide what an absent value means.
    internal static (string? TaskId, string? TaskStatus) ParseTask(string json)
    {
        var task = JsonSerializer.Deserialize<WireTask>(json, JsonOptions);
        return (task?.TaskId, task?.TaskStatus);
    }

    // The reasons a failed task carries on its status response: error_message and failure.message.
    internal static IReadOnlyList<string> ParseTaskErrors(string json)
    {
        var task = JsonSerializer.Deserialize<WireTask>(json, JsonOptions);
        return NonBlank([task?.ErrorMessage, task?.Failure?.Message]);
    }

    // A converted document's result carries errors[]; a failed task's (TaskFailureResult) carries
    // failure.message instead. Both are read, so either shape yields its reasons.
    internal static DoclingConvertResult ParseResult(string json)
    {
        var result = JsonSerializer.Deserialize<WireResult>(json, JsonOptions);
        var errors = NonBlank([
            .. result?.Errors?.Select(e => e?.ErrorMessage) ?? [],
            result?.Failure?.Message,
        ]);

        return new DoclingConvertResult(
            result?.Document?.MdContent,
            result?.Status ?? "unknown",
            errors,
            result?.ProcessingTime,
            ServerVersion: null);
    }

    private static List<string> NonBlank(IEnumerable<string?> messages) =>
        messages.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!).ToList();

    // /version is a flat map of package name to version; the server's own entry is the one recorded.
    internal static string? ParseServerVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("docling-serve", out var version)
            && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
    }

    private static class TaskStatuses
    {
        public const string Pending = "pending";
        public const string Started = "started";
        public const string Success = "success";
        public const string Failure = "failure";
    }

    private sealed record WireTask(
        [property: JsonPropertyName("task_id")] string? TaskId,
        [property: JsonPropertyName("task_status")] string? TaskStatus,
        [property: JsonPropertyName("error_message")] string? ErrorMessage,
        [property: JsonPropertyName("failure")] WireFailure? Failure);

    private sealed record WireResult(
        [property: JsonPropertyName("document")] WireDocument? Document,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("errors")] List<WireError?>? Errors,
        [property: JsonPropertyName("processing_time")] double? ProcessingTime,
        [property: JsonPropertyName("failure")] WireFailure? Failure);

    private sealed record WireFailure([property: JsonPropertyName("message")] string? Message);

    private sealed record WireDocument([property: JsonPropertyName("md_content")] string? MdContent);

    private sealed record WireError([property: JsonPropertyName("error_message")] string? ErrorMessage);
}
