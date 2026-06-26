using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.Ai;

public class AiPageRenderServiceTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"ai-render-svc-{Guid.NewGuid()}");
    private readonly IDocumentPageRenderer _renderer = Substitute.For<IDocumentPageRenderer>();
    private readonly AiPageRenderService _sut;
    private const string Hash = "abcd1234ef567890";

    public AiPageRenderServiceTests()
    {
        Directory.CreateDirectory(_cacheDir);
        _renderer.CanRender(Arg.Any<string>()).Returns(true);
        _sut = MakeService(new AiOptions());
    }

    private AiPageRenderService MakeService(AiOptions options) => new(
        _renderer,
        Options.Create(new FileStorageOptions { CachePath = _cacheDir, MediaPath = _cacheDir }),
        Options.Create(options),
        NullLogger<AiPageRenderService>.Instance);

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetRenderPath_ShardsByHashPrefix_PngUnderAiRenders()
    {
        var path = _sut.GetRenderPath(Hash);
        Assert.Equal(Path.Combine(_cacheDir, "ai-renders", "abcd", $"{Hash}.png"), path);
    }

    [Fact]
    public async Task GetOrCreateAsync_Miss_RendersWritesAndReturnsBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = new byte[] { 1, 2, 3, 4 };
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns(bytes);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", ct);

        Assert.Equal(bytes, result);
        var path = _sut.GetRenderPath(Hash);
        Assert.True(File.Exists(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, ct));
        await _renderer.Received(1).RenderFirstPagePngAsync(
            "/some/file.pdf", "application/pdf", Arg.Any<int>(), ct);
    }

    [Fact]
    public async Task GetOrCreateAsync_Hit_ReturnsCachedBytes_WithoutRendering()
    {
        var ct = TestContext.Current.CancellationToken;
        var cached = new byte[] { 9, 9, 9 };
        var path = _sut.GetRenderPath(Hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, cached, ct);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", ct);

        Assert.Equal(cached, result);
        await _renderer.DidNotReceive().RenderFirstPagePngAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_UnsupportedType_ReturnsNull_NoCacheWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        _renderer.CanRender("text/plain").Returns(false);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.txt", "text/plain", ct);

        Assert.Null(result);
        Assert.False(File.Exists(_sut.GetRenderPath(Hash)));
    }

    [Fact]
    public async Task GetOrCreateAsync_RendererReturnsNull_ReturnsNull_NoCacheWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns((byte[]?)null);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", ct);

        Assert.Null(result);
        Assert.False(File.Exists(_sut.GetRenderPath(Hash)));
    }

    [Fact]
    public async Task GetOrCreateAsync_PassesResolvedMaxEdge_ToRenderer()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = MakeService(new AiOptions { ImageRenderMaxEdge = 2048 });
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns(new byte[] { 1 });

        await sut.GetOrCreateAsync(Hash, "/f.pdf", "application/pdf", ct);

        await _renderer.Received(1).RenderFirstPagePngAsync("/f.pdf", "application/pdf", 2048, ct);
    }
}
