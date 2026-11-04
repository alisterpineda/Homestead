using UglyToad.PdfPig;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// The page count of a source file, read locally: 1 for an image, PdfPig's NumberOfPages for a PDF. Engines
// whose service response carries no reliable count of the SOURCE's pages use it — Azure to spot an F0
// page shortfall, Docling because its Markdown response has no page count at all.
internal static class SourcePageCounter
{
    // Null when the PDF cannot be read: the count is informational, never worth failing an extraction for.
    internal static int? TryCount(string filePath, string contentType, ILogger logger)
    {
        if (contentType is not "application/pdf")
            return 1;

        try
        {
            using var document = PdfDocument.Open(filePath);
            return document.NumberOfPages;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read the page count of {FilePath}", filePath);
            return null;
        }
    }
}
