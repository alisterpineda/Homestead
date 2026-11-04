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
public class DoclingRunner(string endpoint, string? apiKey, HttpClient http, ILogger<DoclingRunner> logger)
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
        var taskId = await SubmitAsync(document, request, ct);
        var status = await WaitForTaskAsync(taskId, ct);

        if (status == TaskStatuses.Failure)
        {
            // The task itself failed (not a document that converted badly). The result endpoint may still
            // carry the reason; read it if it does, but the failure stands either way.
            var errors = await TryReadFailureErrorsAsync(taskId, ct);
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

    private async Task<string> WaitForTaskAsync(string taskId, CancellationToken ct)
    {
        var path = $"/v1/status/poll/{Uri.EscapeDataString(taskId)}?wait={PollWaitSeconds}";
        var consecutiveFailures = 0;

        while (true)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, path, null, ct);
                response.EnsureSuccessStatusCode();
                var status = ParseTask(await response.Content.ReadAsStringAsync(ct)).TaskStatus;
                consecutiveFailures = 0;

                if (status is TaskStatuses.Success or TaskStatuses.Failure)
                    return status;
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

    internal static DoclingConvertResult ParseResult(string json)
    {
        var result = JsonSerializer.Deserialize<WireResult>(json, JsonOptions);
        var errors = result?.Errors?
            .Select(e => e?.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .ToList() ?? [];

        return new DoclingConvertResult(
            result?.Document?.MdContent,
            result?.Status ?? "unknown",
            errors,
            result?.ProcessingTime,
            ServerVersion: null);
    }

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
        public const string Success = "success";
        public const string Failure = "failure";
    }

    private sealed record WireTask(
        [property: JsonPropertyName("task_id")] string? TaskId,
        [property: JsonPropertyName("task_status")] string? TaskStatus);

    private sealed record WireResult(
        [property: JsonPropertyName("document")] WireDocument? Document,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("errors")] List<WireError?>? Errors,
        [property: JsonPropertyName("processing_time")] double? ProcessingTime);

    private sealed record WireDocument([property: JsonPropertyName("md_content")] string? MdContent);

    private sealed record WireError([property: JsonPropertyName("error_message")] string? ErrorMessage);
}
