using PDFtoImage;

namespace Homestead.WebApi.Features.Documents.Thumbnails;

public class PdfThumbnailGenerator : IThumbnailGenerator
{
    public bool CanGenerate(string contentType) =>
        string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public Task<bool> GenerateAsync(string sourcePath, string outputPath, CancellationToken ct)
    {
        using var pdfStream = File.OpenRead(sourcePath);
#pragma warning disable CA1416 // PDFtoImage supports all server platforms (Linux, macOS, Windows)
        using var pageBitmap = Conversion.ToImage(pdfStream, page: (Index)0);
#pragma warning restore CA1416

        return Task.FromResult(ThumbnailHelper.ResizeAndSaveWebp(pageBitmap, outputPath));
    }
}
