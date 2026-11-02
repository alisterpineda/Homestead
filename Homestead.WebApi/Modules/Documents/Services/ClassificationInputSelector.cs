using Homestead.WebApi.Infrastructure.Ai;
using UglyToad.PdfPig;

namespace Homestead.WebApi.Modules.Documents.Services;

// What a classify call sends besides the name and catalogs: the original PDF (Claude reads its text and layout
// itself), the OCR text plus a page-1 render, or the OCR text alone.
public enum ClassificationInputMode { Text, TextAndImage, Pdf }

// The one place that decides a classify call's input mode, so every caller sends the input production would
// actually send. Pure apart from ReadPdfFacts.
public static class ClassificationInputSelector
{
    public const string PdfContentType = "application/pdf";

    // The cheap gates, checked before the file is opened: PDF input on, a scope that benefits from more than the
    // text (a tags-only refresh never sends the PDF — the same cost rule as the image), a PDF, and within the size cap.
    public static bool IsPdfCandidate(
        AiInputSettings settings, ClassificationScope scope, string contentType, long fileSize) =>
        settings.PdfInput
        && scope.ShouldAttachImage()
        && string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase)
        && fileSize > 0
        && fileSize <= settings.PdfInputMaxBytes;

    // pageCount null = unknown (the file couldn't be read as a PDF), which never qualifies. An encrypted PDF
    // doesn't either: the API can't read it, and the text + render path already copes with whatever OCR managed.
    public static ClassificationInputMode Select(
        AiInputSettings settings, ClassificationScope scope, string contentType, long fileSize,
        int? pageCount, bool isEncrypted)
    {
        if (IsPdfCandidate(settings, scope, contentType, fileSize)
            && pageCount is >= 1 && pageCount <= settings.PdfInputMaxPages
            && !isEncrypted)
            return ClassificationInputMode.Pdf;

        return SelectWithoutPdf(settings, scope);
    }

    // The mode when the PDF is not an option — outside the gates, or rejected by the API (DocumentClassifier's
    // fallback): text plus a page render when the image is on and the scope wants it, else text alone.
    public static ClassificationInputMode SelectWithoutPdf(AiInputSettings settings, ClassificationScope scope) =>
        settings.ImageInput && scope.ShouldAttachImage()
            ? ClassificationInputMode.TextAndImage
            : ClassificationInputMode.Text;

    // Page count and encryption from PdfPig (already used by the OCR engines). Null when the file isn't a readable
    // PDF — including one that needs a password, which PdfPig refuses to open.
    public static (int PageCount, bool IsEncrypted)? ReadPdfFacts(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            return (document.NumberOfPages, document.IsEncrypted);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
