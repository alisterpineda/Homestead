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
    IPdfTextRunner pdfText,
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
    // after the fact — see TesseractTextExtractionMetadata.PagesFromTextLayer. TextLayerExtractor is
    // which tool read the embedded text layer (Poppler, or PdfPig as the fallback).
    private record PdfExtraction(string? Text, int PagesFromTextLayer, int PagesOcred, string? TextLayerExtractor);

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
            return Output(text, pagesFromTextLayer: 0, pagesOcred: 1, textLayerExtractor: null);
        }

        if (contentType is "application/pdf")
        {
            var pdf = await ExtractFromPdfAsync(filePath, ct);
            return Output(pdf.Text, pdf.PagesFromTextLayer, pdf.PagesOcred, pdf.TextLayerExtractor);
        }

        return Output(text: null, pagesFromTextLayer: 0, pagesOcred: 0, textLayerExtractor: null);
    }

    private TextExtractionOutput Output(
        string? text, int pagesFromTextLayer, int pagesOcred, string? textLayerExtractor) =>
        new(text, new TesseractTextExtractionMetadata
        {
            EngineKey = key,
            ExtractedAt = DateTimeOffset.UtcNow,
            Language = _language,
            Dpi = options.Dpi,
            Psm = options.Psm,
            Oem = options.Oem,
            PagesFromTextLayer = pagesFromTextLayer,
            PagesOcred = pagesOcred,
            TextLayerExtractor = textLayerExtractor
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
        string? textLayerExtractor = null;

        try
        {
            // PdfPig stays the authority on page order and count; Poppler only supplies the text.
            using var document = PdfDocument.Open(filePath);
            var sb = new StringBuilder();

            var popplerPages = await TryReadTextLayerAsync(filePath, ct);
            var usePoppler = popplerPages is not null && popplerPages.Count == document.NumberOfPages;
            if (popplerPages is not null && !usePoppler)
            {
                // Embedded form feeds in the text would skew the split; PdfPig's per-page text can't.
                logger.LogWarning(
                    "pdftotext returned {PopplerPages} pages for {FilePath} but the PDF has {PdfPages}; using PdfPig",
                    popplerPages.Count, filePath, document.NumberOfPages);
            }
            // The runner logs its own warning when pdftotext is missing (once per process) or exits
            // non-zero (per file); TryReadTextLayerAsync logs anything it throws.
            textLayerExtractor = usePoppler
                ? TesseractTextExtractionMetadata.PopplerTextLayer
                : TesseractTextExtractionMetadata.PdfPigTextLayer;

            using var pdfStream = File.OpenRead(filePath);

            // By number rather than GetPages(): PdfPig builds a page's letters and Text as it parses it, which
            // is wasted work for every page when Poppler's text is the one used.
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                ct.ThrowIfCancellationRequested();

                // Trailing whitespace only: -layout's leading spaces are the column alignment.
                var pageText = usePoppler
                    ? popplerPages![pageNumber - 1].TrimEnd()
                    : document.GetPage(pageNumber).Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(pageText);
                    pagesFromTextLayer++;
                    continue;
                }

                // No embedded text on this page — render as image and OCR.
                var ocrText = await TryOcrPageAsync(pdfStream, pageNumber, filePath, ct);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(ocrText);
                    pagesOcred++;
                }
            }

            return new PdfExtraction(
                sb.Length > 0 ? TrimText(sb.ToString()) : null, pagesFromTextLayer, pagesOcred, textLayerExtractor);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from PDF {FilePath}", filePath);
            return new PdfExtraction(null, pagesFromTextLayer, pagesOcred, textLayerExtractor);
        }
    }

    // The runner reports a missing or failing pdftotext as null, but anything it throws must still fall
    // back to PdfPig: inside ExtractFromPdfAsync's catch-all it would cost the PDF all of its text.
    private async Task<IReadOnlyList<string>?> TryReadTextLayerAsync(string filePath, CancellationToken ct)
    {
        try
        {
            return await pdfText.ExtractPagesAsync(filePath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "pdftotext failed for {FilePath}; using PdfPig", filePath);
            return null;
        }
    }

    // Drops leading blank LINES but not the first line's indentation, which in -layout output is
    // column alignment like any other line's.
    private static string TrimText(string text)
    {
        var trimmed = text.TrimEnd();
        var firstContent = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '\n')
                firstContent = i + 1;
            else if (!char.IsWhiteSpace(trimmed[i]))
                break;
        }
        return trimmed[firstContent..];
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
