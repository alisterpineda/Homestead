using System.Text;
using UglyToad.PdfPig;

namespace HomeSuiteHome.WebApi.TextExtraction;

public class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : ITextExtractor
{
    public bool CanExtract(string contentType)
        => contentType is "application/pdf";

    public Task<string?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            var sb = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();

                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(pageText);
                }
            }

            var result = sb.Length > 0 ? sb.ToString().Trim() : null;
            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from PDF {FilePath}", filePath);
            return Task.FromResult<string?>(null);
        }
    }
}
