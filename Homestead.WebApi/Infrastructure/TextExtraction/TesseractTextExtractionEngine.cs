using System.Text;
using Homestead.WebApi.Domain.Settings;
using Microsoft.Extensions.Options;
using PDFtoImage;
using UglyToad.PdfPig;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class TesseractTextExtractionEngine(
    ITesseractRunner tesseract, IOptions<TesseractOptions> options, ILogger<TesseractTextExtractionEngine> logger)
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

    // The Tesseract language argument ("eng" or "eng+fra"), computed once from configured languages.
    // Folds in the deleted TesseractLanguageResolver's logic; defaults to "eng" when none are configured.
    private readonly string _language = ResolveLanguage(options.Value.Languages);

    // DPI used to rasterize PDF pages, and passed through to Tesseract as --dpi for those pages.
    private readonly int _dpi = options.Value.Dpi;

    private static string ResolveLanguage(List<TesseractLanguage> languages) =>
        languages is { Count: > 0 }
            ? string.Join("+", languages.Select(l => l.ToString().ToLowerInvariant()))
            : TesseractLanguage.Eng.ToString().ToLowerInvariant();

    public async Task<string?> ExtractTextAsync(string filePath, string contentType, CancellationToken ct)
    {
        if (SupportedImageTypes.Contains(contentType))
            return await ExtractFromImageAsync(filePath, ct);

        if (contentType is "application/pdf")
            return await ExtractFromPdfAsync(filePath, ct);

        return null;
    }

    private async Task<string?> ExtractFromImageAsync(string filePath, CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(filePath);
        return await tesseract.OcrAsync(fileStream, _language, dpi: null, ct);
    }

    private async Task<string?> ExtractFromPdfAsync(string filePath, CancellationToken ct)
    {
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
                    continue;
                }

                // No embedded text on this page — render as image and OCR.
                var ocrText = await TryOcrPageAsync(pdfStream, page.Number, filePath, ct);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(ocrText);
                }
            }

            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from PDF {FilePath}", filePath);
            return null;
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
            Conversion.SavePng(pngStream, pdfStream, (Index)(pageNumber - 1), options: new(Dpi: _dpi), leaveOpen: true);
#pragma warning restore CA1416
            pngStream.Position = 0;

            return await tesseract.OcrAsync(pngStream, _language, dpi: _dpi, ct);
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
