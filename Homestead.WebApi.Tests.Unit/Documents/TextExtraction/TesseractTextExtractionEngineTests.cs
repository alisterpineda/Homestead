using Homestead.WebApi.Features.Documents.TextExtraction;
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
        _sut = new TesseractTextExtractionEngine(_runner, Substitute.For<ILogger<TesseractTextExtractionEngine>>());
        _tempFile = Path.GetTempFileName();
    }

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
        var result = await _sut.ExtractTextAsync(_tempFile, contentType, null, CancellationToken.None);

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

        var result = await _sut.ExtractTextAsync(_tempFile, contentType, null, CancellationToken.None);

        Assert.Equal("ocr text", result);
        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), "eng", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithLanguageConfig_PassesLanguageToRunner()
    {
        var config = """{"languages":["Fra","Deu"]}""";
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("texte");

        await _sut.ExtractTextAsync(_tempFile, "image/png", config, CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), "fra+deu", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithNullConfig_UsesDefaultLanguage()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("text");

        await _sut.ExtractTextAsync(_tempFile, "image/png", null, CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), "eng", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_RunnerReturnsNull_ReturnsNull()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _sut.ExtractTextAsync(_tempFile, "image/png", null, CancellationToken.None);

        Assert.Null(result);
    }
}
