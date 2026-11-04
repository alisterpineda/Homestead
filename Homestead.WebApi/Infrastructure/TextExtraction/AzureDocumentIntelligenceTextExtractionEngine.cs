using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Unlike Tesseract, this engine takes no hybrid path: the whole file goes to Azure, embedded text layer
// or not. That is the point for born-digital documents — prebuilt-layout returns Markdown with HTML table
// syntax (<table>/<tr>/<td>, rowspan/colspan preserved), where PdfPig's page.Text returns badly-spaced
// prose that loses the table structure entirely.
public class AzureDocumentIntelligenceTextExtractionEngine(
    string key,
    OcrEngineOptions options,
    IAzureDocumentIntelligenceRunner runner,
    ILogger<AzureDocumentIntelligenceTextExtractionEngine> logger)
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
    public TextExtractionEngineType Type => TextExtractionEngineType.AzureDocumentIntelligence;
    public string? ConfigurationError { get; } = OcrEngineConfiguration.Validate(key, options);
    public bool IsConfigured => ConfigurationError is null;

    public TimeSpan? JobTimeout => options.JobTimeout;

    public async Task<TextExtractionOutput> ExtractTextAsync(
        string filePath, string contentType, CancellationToken ct)
    {
        // Configuration is checked before the content-type shortcut: an engine that cannot run must say
        // so, not report a successful no-op that looks like "Azure ran and found nothing".
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"Text extraction engine '{key}' is not configured: {ConfigurationError}");

        var format = options.ResolveOutputContentFormat();

        if (!SupportedContentTypes.Contains(contentType))
            return Output(text: null, format, pageCount: 0);

        await using var stream = File.OpenRead(filePath);
        var result = await runner.AnalyzeAsync(stream, options.ModelId, format, ct);

        WarnOnPageShortfall(filePath, contentType, result.PageCount);

        return Output(result.Content, format, result.PageCount);
    }

    private TextExtractionOutput Output(string? text, AzureOutputContentFormat format, int pageCount) =>
        new(text, new AzureDocumentIntelligenceTextExtractionMetadata
        {
            EngineKey = key,
            ExtractedAt = DateTimeOffset.UtcNow,
            ModelId = options.ModelId,
            OutputContentFormat = format,
            PageCount = pageCount
        });

    // An F0 (free tier) resource analyzes only the first 2 pages and reports success — IsConfigured is
    // true, extraction "succeeds", and you silently get 2 pages of a 30-page statement. Nothing in config
    // reveals the tier, so the only signal is analyzed-vs-source page count. Mirrors the TextTruncated
    // warning in ParseAccountStatementJobHandler; PageCount in the metadata makes it durable.
    private void WarnOnPageShortfall(string filePath, string contentType, int analyzedPages)
    {
        var sourcePages = SourcePageCounter.TryCount(filePath, contentType, logger);
        if (sourcePages is int total && analyzedPages > 0 && analyzedPages < total)
        {
            logger.LogWarning(
                "Engine '{Key}' analyzed only {AnalyzedPages} of {SourcePages} pages of {FilePath}. "
                + "An F0 (free) Document Intelligence resource caps analysis at 2 pages; extracted text is incomplete.",
                key, analyzedPages, total, filePath);
        }
    }
}
