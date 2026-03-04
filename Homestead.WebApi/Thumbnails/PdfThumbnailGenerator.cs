using PDFtoImage;
using SkiaSharp;

namespace Homestead.WebApi.Thumbnails;

public class PdfThumbnailGenerator : IThumbnailGenerator
{
    public bool CanGenerate(string contentType) =>
        string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public Task<bool> GenerateAsync(string sourcePath, string outputPath, CancellationToken ct)
    {
        using var pdfStream = File.OpenRead(sourcePath);
        using var pageBitmap = Conversion.ToImage(pdfStream, page: (Index)0);

        return Task.FromResult(ThumbnailHelper.ResizeAndSaveWebp(pageBitmap, outputPath));
    }
}
