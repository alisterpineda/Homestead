using System.Text;
using PDFtoImage;
using UglyToad.PdfPig;

namespace Homestead.WebApi.TextExtraction;

public class PdfTextExtractor(TesseractRunner tesseract, ILogger<PdfTextExtractor> logger) : ITextExtractor
{
    public bool CanExtract(string contentType)
        => contentType is "application/pdf";

    public async Task<string?> ExtractTextAsync(string filePath, CancellationToken ct)
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

                // No embedded text on this page — render as image and OCR
                pdfStream.Position = 0;
                using var pngStream = new MemoryStream();
#pragma warning disable CA1416 // PDFtoImage supports all server platforms (Linux, macOS, Windows)
                Conversion.SavePng(pngStream, pdfStream, (Index)(page.Number - 1), leaveOpen: true);
#pragma warning restore CA1416
                pngStream.Position = 0;

                var ocrText = await tesseract.OcrAsync(pngStream, ct);
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
