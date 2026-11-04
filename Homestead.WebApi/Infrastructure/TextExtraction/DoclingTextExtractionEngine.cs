using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Processing;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Table-aware, local extraction through a docling-serve sidecar: layout analysis plus table-structure
// recognition, returned as Markdown with pipe tables. Like Azure it takes no hybrid path — the whole file
// goes to the sidecar, which reads a PDF's text layer itself and OCRs only what lacks one (unless
// ForceOcr). Unlike Azure it is free and keeps documents on the host.
//
// Slow on CPU (seconds to tens of seconds per page), so a Docling instance should set its own JobTimeout:
// the job budget the handlers declare. Every conversion is sent a document_timeout — the deadline
// TextExtractionHelper enforces for that budget, or for the global Processing:JobTimeout when the
// instance sets none — so a conversion can never outlive the helper's patience by more than the time it
// sat in the sidecar's queue.
//
// That queue time is the limit of the guarantee: document_timeout counts conversion only, and nothing
// here cancels a task the helper abandons (no per-task cancel is known in docling-serve's API), so a task abandoned
// while queued still runs, up to document_timeout, when its turn comes. Keep Processing:MaxConcurrency
// near the sidecar's worker count so jobs do not queue behind each other inside it.
public class DoclingTextExtractionEngine(
    string key,
    OcrEngineOptions options,
    IOptions<JobProcessingOptions> processing,
    IDoclingRunner runner,
    ILogger<DoclingTextExtractionEngine> logger)
    : ITextExtractionEngine
{
    private static readonly HashSet<string> SupportedContentTypes =
    [
        "image/png",
        "image/jpeg",
        "image/tiff",
        "image/bmp",
        "application/pdf"
    ];

    public string Key => key;
    public TextExtractionEngineType Type => TextExtractionEngineType.Docling;
    public string? ConfigurationError { get; } = OcrEngineConfiguration.Validate(key, options);
    public bool IsConfigured => ConfigurationError is null;

    public TimeSpan? JobTimeout => options.JobTimeout;

    // Validation accepts either case; the sidecar is sent its own spelling.
    private string? TableMode => options.TableMode?.ToLowerInvariant();

    public async Task<TextExtractionOutput> ExtractTextAsync(
        string filePath, string contentType, CancellationToken ct)
    {
        // Checked before the content-type shortcut, as Azure does: an engine that cannot run must say so
        // rather than report a no-op that reads as "Docling ran and found nothing".
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"Text extraction engine '{key}' is not configured: {ConfigurationError}");

        if (!SupportedContentTypes.Contains(contentType))
        {
            return Output(text: null, DoclingTextExtractionMetadata.UnsupportedContentTypeStatus,
                pageCount: 0, processingSeconds: null, serverVersion: null);
        }

        var request = new DoclingConvertRequest(
            Path.GetFileName(filePath),
            contentType,
            TableMode,
            options.ForceOcr,
            DocumentTimeout());

        DoclingConvertResult result;
        await using (var stream = File.OpenRead(filePath))
            result = await runner.ConvertAsync(stream, request, ct);

        if (result.Status is not (DoclingTextExtractionMetadata.SuccessStatus
                or DoclingTextExtractionMetadata.PartialSuccessStatus)
            || string.IsNullOrWhiteSpace(result.Markdown))
        {
            throw new DoclingConversionException(result.Status, result.Errors);
        }

        if (result.Status == DoclingTextExtractionMetadata.PartialSuccessStatus)
        {
            logger.LogWarning(
                "Engine '{Key}' converted {FilePath} only partially; some content is missing from the text: {Errors}",
                key, filePath, result.Errors.Count == 0 ? "(no reason given)" : string.Join("; ", result.Errors));
        }

        var pageCount = SourcePageCounter.TryCount(filePath, contentType, logger) ?? 0;

        return Output(result.Markdown.Trim(), result.Status, pageCount, result.ProcessingSeconds, result.ServerVersion);
    }

    // The same deadline TextExtractionHelper enforces, from the same budget: this instance's JobTimeout,
    // else the global one. Always set, so the sidecar never converts without a limit.
    private TimeSpan DocumentTimeout() =>
        TextExtractionHelper.ExtractionDeadline(
            OcrEngineOptions.UsableJobTimeout(options.JobTimeout) ?? processing.Value.ResolveJobTimeout());

    private TextExtractionOutput Output(
        string? text, string status, int pageCount, double? processingSeconds, string? serverVersion) =>
        new(text, new DoclingTextExtractionMetadata
        {
            EngineKey = key,
            ExtractedAt = DateTimeOffset.UtcNow,
            ServerVersion = serverVersion,
            TableMode = TableMode,
            ForceOcr = options.ForceOcr,
            PageCount = pageCount,
            ConversionStatus = status,
            ProcessingSeconds = processingSeconds
        });
}
