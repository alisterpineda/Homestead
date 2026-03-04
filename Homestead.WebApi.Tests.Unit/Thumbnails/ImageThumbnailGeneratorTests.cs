using Homestead.WebApi.Thumbnails;
using SkiaSharp;

namespace Homestead.WebApi.Tests.Unit.Thumbnails;

public class ImageThumbnailGeneratorTests : IDisposable
{
    private readonly ImageThumbnailGenerator _generator = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"thumb-test-{Guid.NewGuid()}");

    public ImageThumbnailGeneratorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/bmp")]
    [InlineData("image/tiff")]
    [InlineData("image/webp")]
    public void CanGenerate_ReturnsTrueForSupportedTypes(string contentType)
    {
        Assert.True(_generator.CanGenerate(contentType));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    public void CanGenerate_ReturnsFalseForUnsupportedTypes(string contentType)
    {
        Assert.False(_generator.CanGenerate(contentType));
    }

    [Fact]
    public async Task GenerateAsync_CreatesWebpThumbnail()
    {
        var sourcePath = CreateTestPng(100, 100);
        var outputPath = Path.Combine(_tempDir, "output.webp");

        var result = await _generator.GenerateAsync(sourcePath, outputPath, CancellationToken.None);

        Assert.True(result);
        Assert.True(File.Exists(outputPath));

        // Verify it's a valid WebP by decoding it
        using var bitmap = SKBitmap.Decode(outputPath);
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }

    [Fact]
    public async Task GenerateAsync_RespectsMaxDimensions()
    {
        var sourcePath = CreateTestPng(1000, 2000);
        var outputPath = Path.Combine(_tempDir, "output-large.webp");

        var result = await _generator.GenerateAsync(sourcePath, outputPath, CancellationToken.None);

        Assert.True(result);

        using var bitmap = SKBitmap.Decode(outputPath);
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width <= ThumbnailHelper.MaxWidth);
        Assert.True(bitmap.Height <= ThumbnailHelper.MaxHeight);
    }

    [Fact]
    public void CalculateSize_SmallImage_ReturnsOriginal()
    {
        var (w, h) = ThumbnailHelper.CalculateSize(100, 100);
        Assert.Equal(100, w);
        Assert.Equal(100, h);
    }

    [Fact]
    public void CalculateSize_LargeImage_ScalesDown()
    {
        var (w, h) = ThumbnailHelper.CalculateSize(1200, 1600);
        Assert.True(w <= ThumbnailHelper.MaxWidth);
        Assert.True(h <= ThumbnailHelper.MaxHeight);
    }

    private string CreateTestPng(int width, int height)
    {
        var path = Path.Combine(_tempDir, $"test-{width}x{height}.png");
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
        return path;
    }
}
