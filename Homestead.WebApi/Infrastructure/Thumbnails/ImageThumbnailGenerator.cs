using SkiaSharp;

namespace Homestead.WebApi.Infrastructure.Thumbnails;

public class ImageThumbnailGenerator : IThumbnailGenerator
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/webp"
    };

    public bool CanGenerate(string contentType) => SupportedTypes.Contains(contentType);

    public Task<bool> GenerateAsync(string sourcePath, string outputPath, CancellationToken ct)
    {
        using var input = SKBitmap.Decode(sourcePath);
        if (input is null)
            return Task.FromResult(false);

        return Task.FromResult(ThumbnailHelper.ResizeAndSaveWebp(input, outputPath));
    }
}
