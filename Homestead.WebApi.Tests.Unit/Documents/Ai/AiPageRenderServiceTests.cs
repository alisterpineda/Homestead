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
    private const int MaxEdge = 1024;

    public AiPageRenderServiceTests()
    {
        Directory.CreateDirectory(_cacheDir);
        _renderer.CanRender(Arg.Any<string>()).Returns(true);
        _sut = new AiPageRenderService(
            _renderer,
            Options.Create(new FileStorageOptions { CachePath = _cacheDir, MediaPath = _cacheDir }),
            NullLogger<AiPageRenderService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetRenderPath_ShardsByHashPrefix_EdgeInFilename_PngUnderAiRenders()
    {
        var path = _sut.GetRenderPath(Hash, MaxEdge);
        Assert.Equal(Path.Combine(_cacheDir, "ai-renders", "abcd", $"{Hash}-{MaxEdge}.png"), path);
    }

    // The edge is part of the cache key: providers can carry different ImageRenderMaxEdge values, and a
    // hash-only key would hand one provider's cached resolution to another.
    [Fact]
    public void GetRenderPath_DifferentEdges_DistinctCachePaths()
    {
        Assert.NotEqual(_sut.GetRenderPath(Hash, 1024), _sut.GetRenderPath(Hash, 2048));
    }

    [Fact]
    public async Task GetOrCreateAsync_Miss_RendersWritesAndReturnsBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = new byte[] { 1, 2, 3, 4 };
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns(bytes);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", MaxEdge, ct);

        Assert.Equal(bytes, result);
        var path = _sut.GetRenderPath(Hash, MaxEdge);
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
        var path = _sut.GetRenderPath(Hash, MaxEdge);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, cached, ct);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", MaxEdge, ct);

        Assert.Equal(cached, result);
        await _renderer.DidNotReceive().RenderFirstPagePngAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // A render cached at one edge is a MISS for another edge — the stale-resolution guard.
    [Fact]
    public async Task GetOrCreateAsync_CachedAtOtherEdge_RendersFresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var cached = new byte[] { 9, 9, 9 };
        var pathAt1024 = _sut.GetRenderPath(Hash, 1024);
        Directory.CreateDirectory(Path.GetDirectoryName(pathAt1024)!);
        await File.WriteAllBytesAsync(pathAt1024, cached, ct);
        var fresh = new byte[] { 1, 2 };
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), 2048, ct)
            .Returns(fresh);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", 2048, ct);

        Assert.Equal(fresh, result);
        await _renderer.Received(1).RenderFirstPagePngAsync("/some/file.pdf", "application/pdf", 2048, ct);
    }

    [Fact]
    public async Task GetOrCreateAsync_UnsupportedType_ReturnsNull_NoCacheWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        _renderer.CanRender("text/plain").Returns(false);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.txt", "text/plain", MaxEdge, ct);

        Assert.Null(result);
        Assert.False(File.Exists(_sut.GetRenderPath(Hash, MaxEdge)));
    }

    [Fact]
    public async Task GetOrCreateAsync_RendererReturnsNull_ReturnsNull_NoCacheWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns((byte[]?)null);

        var result = await _sut.GetOrCreateAsync(Hash, "/some/file.pdf", "application/pdf", MaxEdge, ct);

        Assert.Null(result);
        Assert.False(File.Exists(_sut.GetRenderPath(Hash, MaxEdge)));
    }

    [Fact]
    public async Task GetOrCreateAsync_PassesGivenMaxEdge_ToRenderer()
    {
        var ct = TestContext.Current.CancellationToken;
        _renderer.RenderFirstPagePngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns(new byte[] { 1 });

        await _sut.GetOrCreateAsync(Hash, "/f.pdf", "application/pdf", 2048, ct);

        await _renderer.Received(1).RenderFirstPagePngAsync("/f.pdf", "application/pdf", 2048, ct);
    }
}
