using System.Text;
using Homestead.WebApi.TextExtraction.Configurations;
using PDFtoImage;
using UglyToad.PdfPig;

namespace Homestead.WebApi.TextExtraction;

public class TesseractTextExtractionEngine(ITesseractRunner tesseract, ILogger<TesseractTextExtractionEngine> logger)
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

    public async Task<string?> ExtractTextAsync(string filePath, string contentType, string? configuration, CancellationToken ct)
    {
        if (SupportedImageTypes.Contains(contentType))
            return await ExtractFromImageAsync(filePath, configuration, ct);

        if (contentType is "application/pdf")
            return await ExtractFromPdfAsync(filePath, configuration, ct);

        return null;
    }

    private async Task<string?> ExtractFromImageAsync(string filePath, string? configuration, CancellationToken ct)
    {
        var language = TesseractLanguageResolver.Resolve(configuration);
        await using var fileStream = File.OpenRead(filePath);
        return await tesseract.OcrAsync(fileStream, language, ct);
    }

    private async Task<string?> ExtractFromPdfAsync(string filePath, string? configuration, CancellationToken ct)
    {
        try
        {
            var language = TesseractLanguageResolver.Resolve(configuration);

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

                // No embedded text on this page — render as image and OCR
                pdfStream.Position = 0;
                using var pngStream = new MemoryStream();
#pragma warning disable CA1416 // PDFtoImage supports all server platforms (Linux, macOS, Windows)
                Conversion.SavePng(pngStream, pdfStream, (Index)(page.Number - 1), leaveOpen: true);
#pragma warning restore CA1416
                pngStream.Position = 0;

                var ocrText = await tesseract.OcrAsync(pngStream, language, ct);
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
}
