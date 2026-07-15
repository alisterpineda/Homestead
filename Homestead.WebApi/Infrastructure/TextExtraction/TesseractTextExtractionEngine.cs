using System.Text;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using PDFtoImage;
using UglyToad.PdfPig;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractTextExtractionEngine(
    string key,
    OcrEngineOptions options,
    ITesseractRunner tesseract,
    ILogger<TesseractTextExtractionEngine> logger)
    : ITextExtractionEngine
{
    private static readonly HashSet<string> SupportedImageTypes =
    [
        "image/png",
        "image/jpeg",
        "image/tiff",
        "image/bmp",
        "image/gif"
    ];

    // Text and how it was obtained, per page. The counts are what make the hybrid PDF branch visible
    // after the fact — see TesseractTextExtractionMetadata.PagesFromTextLayer.
    private record PdfExtraction(string? Text, int PagesFromTextLayer, int PagesOcred);

    // The Tesseract language argument ("eng" or "eng+fra"), computed once from configured languages.
    private readonly string _language = ResolveLanguage(options.Languages);

    public string Key => key;
    public TextExtractionEngineType Type => TextExtractionEngineType.Tesseract;
    public string? ConfigurationError { get; } = OcrEngineConfiguration.Validate(key, options);
    public bool IsConfigured => ConfigurationError is null;

    private static string ResolveLanguage(List<TesseractLanguage> languages) =>
        languages is { Count: > 0 }
            ? string.Join("+", languages.Select(l => l.ToString().ToLowerInvariant()))
            : TesseractLanguage.Eng.ToString().ToLowerInvariant();

    private TesseractRunOptions RunOptions(int? dpi) =>
        new(_language, options.TessDataPath, dpi, options.Psm, options.Oem);

    public async Task<TextExtractionOutput> ExtractTextAsync(
        string filePath, string contentType, CancellationToken ct)
    {
        if (SupportedImageTypes.Contains(contentType))
        {
            var text = await ExtractFromImageAsync(filePath, ct);
            return Output(text, pagesFromTextLayer: 0, pagesOcred: 1);
        }

        if (contentType is "application/pdf")
        {
            var pdf = await ExtractFromPdfAsync(filePath, ct);
            return Output(pdf.Text, pdf.PagesFromTextLayer, pdf.PagesOcred);
        }

        return Output(text: null, pagesFromTextLayer: 0, pagesOcred: 0);
    }

    private TextExtractionOutput Output(string? text, int pagesFromTextLayer, int pagesOcred) =>
        new(text, new TesseractTextExtractionMetadata
        {
            EngineKey = key,
            ExtractedAt = DateTimeOffset.UtcNow,
            Language = _language,
            Dpi = options.Dpi,
            Psm = options.Psm,
            Oem = options.Oem,
            PagesFromTextLayer = pagesFromTextLayer,
            PagesOcred = pagesOcred
        });

    private async Task<string?> ExtractFromImageAsync(string filePath, CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(filePath);
        return await tesseract.OcrAsync(fileStream, RunOptions(dpi: null), ct);
    }

    private async Task<PdfExtraction> ExtractFromPdfAsync(string filePath, CancellationToken ct)
    {
        var pagesFromTextLayer = 0;
        var pagesOcred = 0;

        try
        {
            using var document = PdfDocument.Open(filePath);
            var sb = new StringBuilder();

            using var pdfStream = File.OpenRead(filePath);

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();

                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(pageText);
                    pagesFromTextLayer++;
                    continue;
                }

                // No embedded text on this page — render as image and OCR.
                var ocrText = await TryOcrPageAsync(pdfStream, page.Number, filePath, ct);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(ocrText);
                    pagesOcred++;
                }
            }

            return new PdfExtraction(
                sb.Length > 0 ? sb.ToString().Trim() : null, pagesFromTextLayer, pagesOcred);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from PDF {FilePath}", filePath);
            return new PdfExtraction(null, pagesFromTextLayer, pagesOcred);
        }
    }

    // Per-page failure (tesseract missing, render error, etc.) must not discard
    // text already extracted from other pages.
    private async Task<string?> TryOcrPageAsync(
        FileStream pdfStream, int pageNumber, string filePath, CancellationToken ct)
    {
        try
        {
            pdfStream.Position = 0;
            using var pngStream = new MemoryStream();
#pragma warning disable CA1416 // PDFtoImage supports all server platforms (Linux, macOS, Windows)
            Conversion.SavePng(pngStream, pdfStream, (Index)(pageNumber - 1), options: new(Dpi: options.Dpi), leaveOpen: true);
#pragma warning restore CA1416
            pngStream.Position = 0;

            return await tesseract.OcrAsync(pngStream, RunOptions(options.Dpi), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "OCR fallback failed for page {PageNumber} of {FilePath}; continuing with remaining pages",
                pageNumber, filePath);
            return null;
        }
    }
}
