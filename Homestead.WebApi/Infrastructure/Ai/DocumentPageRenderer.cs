using Homestead.WebApi.Infrastructure.Thumbnails;
using PDFtoImage;
using SkiaSharp;

namespace Homestead.WebApi.Infrastructure.Ai;

public class DocumentPageRenderer : IDocumentPageRenderer
{
    // Mirrors ThumbnailService's supported set: PDFs (page 0) plus the raster image types SkiaSharp decodes.
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/webp",
        "application/pdf"
    };

    public bool CanRender(string contentType) => SupportedContentTypes.Contains(contentType);

    public Task<byte[]?> RenderFirstPagePngAsync(
        string sourcePath, string contentType, int maxEdge, CancellationToken ct)
    {
        if (!CanRender(contentType))
            return Task.FromResult<byte[]?>(null);

        using var bitmap = Decode(sourcePath, contentType);
        if (bitmap is null)
            return Task.FromResult<byte[]?>(null);

        return Task.FromResult(ThumbnailHelper.ResizeToPng(bitmap, maxEdge));
    }

    // Decodes the first page/frame to an SKBitmap: PDFtoImage for PDFs (page 0, as in PdfThumbnailGenerator),
    // SkiaSharp's native decode for raster images.
    private static SKBitmap? Decode(string sourcePath, string contentType)
    {
        if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var pdfStream = File.OpenRead(sourcePath);
#pragma warning disable CA1416 // PDFtoImage supports all server platforms (Linux, macOS, Windows)
            return Conversion.ToImage(pdfStream, page: (Index)0);
#pragma warning restore CA1416
        }

        return SKBitmap.Decode(sourcePath);
    }
}
