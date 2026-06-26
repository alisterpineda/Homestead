using SkiaSharp;

namespace Homestead.WebApi.Infrastructure.Thumbnails;

public static class ThumbnailHelper
{
    public const int MaxWidth = 600;
    public const int MaxHeight = 800;
    public const int WebpQuality = 75;

    public static bool ResizeAndSaveWebp(SKBitmap source, string outputPath)
    {
        var (width, height) = CalculateSize(source.Width, source.Height);

        using var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
            return false;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);

        return true;
    }

    public static (int width, int height) CalculateSize(int originalWidth, int originalHeight)
    {
        if (originalWidth <= MaxWidth && originalHeight <= MaxHeight)
            return (originalWidth, originalHeight);

        var ratioW = (double)MaxWidth / originalWidth;
        var ratioH = (double)MaxHeight / originalHeight;
        var ratio = Math.Min(ratioW, ratioH);

        return ((int)(originalWidth * ratio), (int)(originalHeight * ratio));
    }

    // Resizes a decoded page so its long edge fits maxEdge and returns lossless PNG bytes — the AI page
    // render sibling of ResizeAndSaveWebp. PNG (not WebP) because the model reads the render: lossless
    // keeps small text/letterhead crisp, and the ~280-token image cost is fixed regardless of file size.
    // Returns null if the resize fails. Caller owns persisting the bytes (AiPageRenderService).
    public static byte[]? ResizeToPng(SKBitmap source, int maxEdge)
    {
        var (width, height) = CalculateSizeForMaxEdge(source.Width, source.Height, maxEdge);

        using var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
            return null;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        // Encode returns null on an encode failure (e.g. a degenerate bitmap) — degrade gracefully like the
        // resized null-guard above rather than NRE on data.ToArray().
        if (data is null)
            return null;
        return data.ToArray();
    }

    // Scales (w, h) down so the longer edge equals maxEdge, preserving aspect ratio; returns the source
    // dimensions unchanged when it already fits. A single long-edge bound (unlike CalculateSize's WxH box)
    // because render fidelity is governed by one configurable max-edge.
    public static (int width, int height) CalculateSizeForMaxEdge(int originalWidth, int originalHeight, int maxEdge)
    {
        var longEdge = Math.Max(originalWidth, originalHeight);
        if (longEdge <= maxEdge)
            return (originalWidth, originalHeight);

        var ratio = (double)maxEdge / longEdge;
        return (Math.Max(1, (int)(originalWidth * ratio)), Math.Max(1, (int)(originalHeight * ratio)));
    }
}
