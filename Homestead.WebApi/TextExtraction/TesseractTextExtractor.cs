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

    public async Task<string?> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(filePath);
        return await tesseract.OcrAsync(fileStream, ct);
    }
}
