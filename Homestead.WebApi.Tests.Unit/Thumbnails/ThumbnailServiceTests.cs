using Homestead.WebApi.Thumbnails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Thumbnails;

public class ThumbnailServiceTests : IDisposable
{
    private readonly string _cachePath = Path.Combine(Path.GetTempPath(), $"thumb-svc-test-{Guid.NewGuid()}");
    private readonly ThumbnailService _service;
    private readonly IThumbnailGenerator _imageGen;
    private readonly IThumbnailGenerator _pdfGen;

    public ThumbnailServiceTests()
    {
        Directory.CreateDirectory(_cachePath);

        _imageGen = Substitute.For<IThumbnailGenerator>();
        _imageGen.CanGenerate("image/png").Returns(true);
        _imageGen.CanGenerate("image/jpeg").Returns(true);

        _pdfGen = Substitute.For<IThumbnailGenerator>();
        _pdfGen.CanGenerate("application/pdf").Returns(true);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:CachePath"] = _cachePath
            })
            .Build();

        var logger = Substitute.For<ILogger<ThumbnailService>>();

        _service = new ThumbnailService(new[] { _imageGen, _pdfGen }, config, logger);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cachePath, recursive: true); } catch { }
    }

    [Fact]
    public void GetThumbnailPath_ReturnsCorrectPath()
    {
        var hash = "abcd1234567890";
        var expected = Path.Combine(_cachePath, "thumbnails", "abcd", $"{hash}.webp");
        Assert.Equal(expected, _service.GetThumbnailPath(hash));
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("application/pdf", true)]
    [InlineData("text/plain", false)]
    [InlineData("application/json", false)]
    public void CanGenerate_ReturnsExpected(string contentType, bool expected)
    {
        Assert.Equal(expected, ThumbnailService.CanGenerate(contentType));
    }

    [Fact]
    public async Task GenerateAsync_UsesCorrectGenerator_ForImage()
    {
        _imageGen.GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await _service.GenerateAsync("/fake/path.png", "image/png", "abcd1234567890", CancellationToken.None);

        await _imageGen.Received(1).GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _pdfGen.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_UsesCorrectGenerator_ForPdf()
    {
        _pdfGen.GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await _service.GenerateAsync("/fake/path.pdf", "application/pdf", "efgh5678901234", CancellationToken.None);

        await _pdfGen.Received(1).GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _imageGen.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_WhenNoGeneratorMatches()
    {
        var result = await _service.GenerateAsync("/fake/path.txt", "text/plain", "xxxx1234567890", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void ThumbnailExists_ReturnsFalse_WhenNotCached()
    {
        Assert.False(_service.ThumbnailExists("nonexistent1234"));
    }

    [Fact]
    public void ThumbnailExists_ReturnsTrue_WhenCached()
    {
        var hash = "test1234567890";
        var path = _service.GetThumbnailPath(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fake");

        Assert.True(_service.ThumbnailExists(hash));
    }
}
