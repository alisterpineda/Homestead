using Homestead.WebApi.Infrastructure.TextExtraction;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class AzureDocumentIntelligenceTextExtractionEngineTests : IDisposable
{
    private readonly IAzureDocumentIntelligenceRunner _runner;
    private readonly AzureDocumentIntelligenceTextExtractionEngine _sut;
    private readonly string _tempFile;

    private const string ValidConfig =
        """{"endpoint":"https://test.cognitiveservices.azure.com","apiKey":"key123"}""";

    public AzureDocumentIntelligenceTextExtractionEngineTests()
    {
        _runner = Substitute.For<IAzureDocumentIntelligenceRunner>();
        _sut = new AzureDocumentIntelligenceTextExtractionEngine(_runner);
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
        var result = await _sut.ExtractTextAsync(_tempFile, contentType, ValidConfig, CancellationToken.None);

        Assert.Null(result);
        await _runner.DidNotReceive().AnalyzeAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    public async Task ExtractTextAsync_SupportedContentType_CallsRunner(string contentType)
    {
        _runner.AnalyzeAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("extracted text");

        var result = await _sut.ExtractTextAsync(_tempFile, contentType, ValidConfig, CancellationToken.None);

        Assert.Equal("extracted text", result);
        await _runner.Received(1).AnalyzeAsync(
            Arg.Any<Stream>(),
            "https://test.cognitiveservices.azure.com",
            "key123",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_NullConfig_ThrowsInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", null, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyConfig_ThrowsInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", "", CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_InvalidJson_ThrowsInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", "not-json", CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_MissingEndpoint_ThrowsInvalidOperation()
    {
        var config = """{"apiKey":"key123"}""";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", config, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_MissingApiKey_ThrowsInvalidOperation()
    {
        var config = """{"endpoint":"https://test.cognitiveservices.azure.com"}""";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", config, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyEndpoint_ThrowsInvalidOperation()
    {
        var config = """{"endpoint":"","apiKey":"key123"}""";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", config, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyApiKey_ThrowsInvalidOperation()
    {
        var config = """{"endpoint":"https://test.cognitiveservices.azure.com","apiKey":""}""";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExtractTextAsync(_tempFile, "application/pdf", config, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractTextAsync_RunnerReturnsNull_ReturnsNull()
    {
        _runner.AnalyzeAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _sut.ExtractTextAsync(_tempFile, "application/pdf", ValidConfig, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_UnsupportedContentType_DoesNotValidateConfig()
    {
        // Even with invalid config, unsupported content type should return null without throwing
        var result = await _sut.ExtractTextAsync(_tempFile, "text/plain", "invalid", CancellationToken.None);

        Assert.Null(result);
    }
}
