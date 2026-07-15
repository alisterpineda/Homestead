using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class TesseractTextExtractionEngineTests : IDisposable
{
    private readonly ITesseractRunner _runner;
    private readonly TesseractTextExtractionEngine _sut;
    private readonly string _tempFile;

    public TesseractTextExtractionEngineTests()
    {
        _runner = Substitute.For<ITesseractRunner>();
        _sut = EngineWith(new OcrEngineOptions());
        _tempFile = Path.GetTempFileName();
    }

    private TesseractTextExtractionEngine EngineWith(OcrEngineOptions options, string key = "tesseract") =>
        new(key, options, _runner, Substitute.For<ILogger<TesseractTextExtractionEngine>>());

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public async Task ExtractTextAsync_UnsupportedContentType_ReturnsNullText(string contentType)
    {
        var result = await _sut.ExtractTextAsync(_tempFile, contentType, CancellationToken.None);

        Assert.Null(result.Text);
        await _runner.DidNotReceive().OcrAsync(
            Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    [InlineData("image/gif")]
    public async Task ExtractTextAsync_SupportedImageType_CallsRunner(string contentType)
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("ocr text");

        var result = await _sut.ExtractTextAsync(_tempFile, contentType, CancellationToken.None);

        Assert.Equal("ocr text", result.Text);
        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(),
            Arg.Is<TesseractRunOptions>(o => o.Language == "eng" && o.Dpi == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithConfiguredLanguages_PassesLanguageToRunner()
    {
        // Languages come from the engine's own OcrEngineOptions instance, not a per-call config blob.
        var engine = EngineWith(new OcrEngineOptions { Languages = [TesseractLanguage.Fra, TesseractLanguage.Deu] });
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("texte");

        await engine.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), Arg.Is<TesseractRunOptions>(o => o.Language == "fra+deu"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithDefaultOptions_UsesDefaultLanguage()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("text");

        await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), Arg.Is<TesseractRunOptions>(o => o.Language == "eng"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_RunnerReturnsNull_ReturnsNullText()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        Assert.Null(result.Text);
    }

    // Each engine instance runs on its own options bag, so the shared, stateless runner has to be told
    // the tessdata path / psm / oem per call — exactly what the old IOptions-capturing runner could not do.
    [Fact]
    public async Task ExtractTextAsync_TwoInstances_EachPassesItsOwnRunOptions()
    {
        var first = EngineWith(new OcrEngineOptions { TessDataPath = "/a", Psm = 6, Oem = 1 }, "first");
        var second = EngineWith(new OcrEngineOptions { TessDataPath = "/b", Psm = 3, Oem = 3 }, "second");
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("text");

        await first.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);
        await second.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(Arg.Any<Stream>(),
            Arg.Is<TesseractRunOptions>(o => o.TessDataPath == "/a" && o.Psm == 6 && o.Oem == 1),
            Arg.Any<CancellationToken>());
        await _runner.Received(1).OcrAsync(Arg.Any<Stream>(),
            Arg.Is<TesseractRunOptions>(o => o.TessDataPath == "/b" && o.Psm == 3 && o.Oem == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_Image_ReportsProvenance()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("text");

        var result = await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal("tesseract", metadata.EngineKey);
        Assert.Equal("eng", metadata.Language);
        Assert.Equal(0, metadata.PagesFromTextLayer);
        Assert.Equal(1, metadata.PagesOcred);
        Assert.Null(metadata.RequestedEngineKey);
    }

    [Fact]
    public void DefaultOptions_IsConfigured()
    {
        Assert.True(_sut.IsConfigured);
        Assert.Null(_sut.ConfigurationError);
        Assert.Equal(TextExtractionEngineType.Tesseract, _sut.Type);
    }

    [Fact]
    public void NoTessDataPath_ReportsUnavailableWithReason()
    {
        var engine = EngineWith(new OcrEngineOptions { TessDataPath = "" });

        Assert.False(engine.IsConfigured);
        Assert.Contains("TessDataPath", engine.ConfigurationError!);
    }
}
