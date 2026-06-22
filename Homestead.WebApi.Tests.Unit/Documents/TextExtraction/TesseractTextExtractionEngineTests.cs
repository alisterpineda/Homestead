using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        _sut = EngineWith(new TesseractOptions());
        _tempFile = Path.GetTempFileName();
    }

    private TesseractTextExtractionEngine EngineWith(TesseractOptions options) =>
        new(_runner, Options.Create(options), Substitute.For<ILogger<TesseractTextExtractionEngine>>());

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public async Task ExtractTextAsync_UnsupportedContentType_ReturnsNull(string contentType)
    {
        var result = await _sut.ExtractTextAsync(_tempFile, contentType, CancellationToken.None);

        Assert.Null(result);
        await _runner.DidNotReceive().OcrAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    [InlineData("image/gif")]
    public async Task ExtractTextAsync_SupportedImageType_CallsRunner(string contentType)
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("ocr text");

        var result = await _sut.ExtractTextAsync(_tempFile, contentType, CancellationToken.None);

        Assert.Equal("ocr text", result);
        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), "eng", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithConfiguredLanguages_PassesLanguageToRunner()
    {
        // Languages now come from TesseractOptions (static config), not a per-call config blob.
        var engine = EngineWith(new TesseractOptions { Languages = [TesseractLanguage.Fra, TesseractLanguage.Deu] });
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("texte");

        await engine.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), "fra+deu", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithDefaultOptions_UsesDefaultLanguage()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("text");

        await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), "eng", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_RunnerReturnsNull_ReturnsNull()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        Assert.Null(result);
    }
}
