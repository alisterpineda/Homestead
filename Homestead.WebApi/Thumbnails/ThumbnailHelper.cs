using SkiaSharp;

namespace Homestead.WebApi.Thumbnails;

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
}
