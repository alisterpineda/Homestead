using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.TextExtraction;

public class TesseractTextExtractor(TesseractRunner tesseract) : ITextExtractor
{
    private static readonly HashSet<string> SupportedContentTypes =
    [
        "image/png",
        "image/jpeg",
        "image/tiff",
        "image/bmp",
        "image/gif"
    ];

    public bool CanExtract(string contentType)
        => SupportedContentTypes.Contains(contentType);

    public async Task<string?> ExtractTextAsync(string filePath, TextExtractionContext context, CancellationToken ct)
    {
        var language = TesseractLanguageResolver.Resolve(context);
        await using var fileStream = File.OpenRead(filePath);
        return await tesseract.OcrAsync(fileStream, language, ct);
    }
}
