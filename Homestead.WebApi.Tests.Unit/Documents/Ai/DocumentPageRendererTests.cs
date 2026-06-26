using Homestead.WebApi.Infrastructure.Ai;
using SkiaSharp;

namespace Homestead.WebApi.Tests.Unit.Documents.Ai;

public class DocumentPageRendererTests : IDisposable
{
    private readonly DocumentPageRenderer _renderer = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ai-render-test-{Guid.NewGuid()}");

    public DocumentPageRendererTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/bmp")]
    [InlineData("image/tiff")]
    [InlineData("image/webp")]
    public void CanRender_TrueForSupportedTypes(string contentType) =>
        Assert.True(_renderer.CanRender(contentType));

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    public void CanRender_FalseForUnsupportedTypes(string contentType) =>
        Assert.False(_renderer.CanRender(contentType));

    [Fact]
    public async Task RenderFirstPagePngAsync_RasterImage_ProducesPngBoundedByMaxEdge()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = CreateTestPng(2000, 1000);

        var bytes = await _renderer.RenderFirstPagePngAsync(source, "image/png", maxEdge: 1024, ct);

        Assert.NotNull(bytes);
        Assert.True(IsPng(bytes!));
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.True(Math.Max(bitmap.Width, bitmap.Height) <= 1024);
        // Long edge scaled to the budget, aspect ratio (2:1) preserved.
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(512, bitmap.Height);
    }

    [Fact]
    public async Task RenderFirstPagePngAsync_SmallImage_NotUpscaled()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = CreateTestPng(300, 200);

        var bytes = await _renderer.RenderFirstPagePngAsync(source, "image/png", maxEdge: 1024, ct);

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(300, bitmap!.Width);
        Assert.Equal(200, bitmap.Height);
    }

    [Fact]
    public async Task RenderFirstPagePngAsync_Pdf_RendersPageZeroToPng()
    {
        var ct = TestContext.Current.CancellationToken;
        var samplePdf = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Homestead.WebApi.Tests.Integration", "TestData", "sample.pdf");
        Assert.SkipWhen(!File.Exists(samplePdf), "Integration test data (sample.pdf) not available");

        var bytes = await _renderer.RenderFirstPagePngAsync(samplePdf, "application/pdf", maxEdge: 1024, ct);

        Assert.NotNull(bytes);
        Assert.True(IsPng(bytes!));
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
        Assert.True(Math.Max(bitmap.Width, bitmap.Height) <= 1024);
    }

    [Fact]
    public async Task RenderFirstPagePngAsync_UnsupportedType_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = CreateTestPng(100, 100);

        var bytes = await _renderer.RenderFirstPagePngAsync(source, "text/plain", maxEdge: 1024, ct);

        Assert.Null(bytes);
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private string CreateTestPng(int width, int height)
    {
        var path = Path.Combine(_tempDir, $"test-{width}x{height}.png");
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
        return path;
    }
}
