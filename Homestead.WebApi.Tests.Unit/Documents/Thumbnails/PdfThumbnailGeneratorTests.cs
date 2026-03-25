using Homestead.WebApi.Infrastructure.Thumbnails;
using SkiaSharp;

namespace Homestead.WebApi.Tests.Unit.Documents.Thumbnails;

public class PdfThumbnailGeneratorTests : IDisposable
{
    private readonly PdfThumbnailGenerator _generator = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"thumb-pdf-test-{Guid.NewGuid()}");

    public PdfThumbnailGeneratorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void CanGenerate_ReturnsTrueForPdf()
    {
        Assert.True(_generator.CanGenerate("application/pdf"));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    public void CanGenerate_ReturnsFalseForNonPdf(string contentType)
    {
        Assert.False(_generator.CanGenerate(contentType));
    }

    [Fact]
    public async Task GenerateAsync_CreatesWebpFromPdf()
    {
        var samplePdf = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Homestead.WebApi.Tests.Integration", "TestData", "sample.pdf");

        Assert.SkipWhen(!File.Exists(samplePdf), "Integration test data (sample.pdf) not available");

        var outputPath = Path.Combine(_tempDir, "pdf-output.webp");

        var result = await _generator.GenerateAsync(samplePdf, outputPath, CancellationToken.None);

        Assert.True(result);
        Assert.True(File.Exists(outputPath));

        using var bitmap = SKBitmap.Decode(outputPath);
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }
}
