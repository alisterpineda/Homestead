using System.Text;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Storage;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup;

public class LocalDirectoryBackupTargetTests : IDisposable
{
    private readonly string _root;
    private readonly LocalDirectoryBackupTarget _sut;

    public LocalDirectoryBackupTargetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "homestead-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _sut = new LocalDirectoryBackupTarget(
            "local",
            new BackupTargetOptions { Kind = BackupTargetKind.Local, Path = _root },
            new FileStorageOptions
            {
                MediaPath = Path.Combine(_root, "..", "media-elsewhere"),
                CachePath = Path.Combine(_root, "..", "cache-elsewhere"),
            });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static Stream Content(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private string Absolute(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void AValidTargetIsAvailable()
    {
        Assert.True(_sut.IsAvailable);
        Assert.Null(_sut.ConfigurationError);
        Assert.Equal(BackupTargetKind.Local, _sut.Kind);
        Assert.Equal(_root, _sut.Location);
    }

    [Fact]
    public async Task PutAsync_WritesTheFileAndCreatesNestedDirectories()
    {
        var handle = await _sut.PutAsync(
            new BackupObjectRef("2025/_versions/Doc (v1).pdf", null),
            Content("hello"),
            "application/pdf",
            TestContext.Current.CancellationToken);

        Assert.Equal("2025/_versions/Doc (v1).pdf", handle);
        Assert.Equal("hello", await File.ReadAllTextAsync(
            Absolute("2025/_versions/Doc (v1).pdf"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_OverwritesInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = new BackupObjectRef("2025/Doc.pdf", null);

        await _sut.PutAsync(target, Content("first"), "application/pdf", ct);
        await _sut.PutAsync(target, Content("second"), "application/pdf", ct);

        Assert.Equal("second", await File.ReadAllTextAsync(Absolute("2025/Doc.pdf"), ct));
    }

    // The temp-file + rename dance must not leave debris behind.
    [Fact]
    public async Task PutAsync_LeavesNoTemporaryFiles()
    {
        await _sut.PutAsync(
            new BackupObjectRef("2025/Doc.pdf", null), Content("hello"), "application/pdf",
            TestContext.Current.CancellationToken);

        var stray = Directory.EnumerateFiles(_root, "*.homestead-tmp-*", SearchOption.AllDirectories);
        Assert.Empty(stray);
    }

    [Fact]
    public async Task MoveAsync_RelocatesTheFile()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.PutAsync(new BackupObjectRef("2025/Doc.pdf", null), Content("payload"), "application/pdf", ct);

        var handle = await _sut.MoveAsync(
            new BackupObjectRef("2025/Doc.pdf", "2025/Doc.pdf"), "2026/Renamed.pdf", ct);

        Assert.Equal("2026/Renamed.pdf", handle);
        Assert.False(File.Exists(Absolute("2025/Doc.pdf")));
        Assert.Equal("payload", await File.ReadAllTextAsync(Absolute("2026/Renamed.pdf"), ct));
    }

    [Fact]
    public async Task MoveAsync_CreatesTheDestinationDirectory()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.PutAsync(new BackupObjectRef("2025/Doc.pdf", null), Content("payload"), "application/pdf", ct);

        await _sut.MoveAsync(
            new BackupObjectRef("2025/Doc.pdf", null), "2030/_versions/Doc (v1).pdf", ct);

        Assert.True(File.Exists(Absolute("2030/_versions/Doc (v1).pdf")));
    }

    // Signals the batch handler to drop the state row so the next run re-copies from scratch.
    [Fact]
    public async Task MoveAsync_MissingSource_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.MoveAsync(
            new BackupObjectRef("2025/Absent.pdf", null), "2026/Absent.pdf",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.PutAsync(new BackupObjectRef("2025/Doc.pdf", null), Content("payload"), "application/pdf", ct);

        await _sut.DeleteAsync(new BackupObjectRef("2025/Doc.pdf", "2025/Doc.pdf"), ct);

        Assert.False(File.Exists(Absolute("2025/Doc.pdf")));
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.DeleteAsync(new BackupObjectRef("2025/NeverExisted.pdf", null), ct);
        await _sut.DeleteAsync(new BackupObjectRef("2025/NeverExisted.pdf", null), ct);
    }

    // An empty year shard left behind forever would clutter the very browsability the mirror exists for.
    [Fact]
    public async Task DeleteAsync_PrunesTheNowEmptyShard()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.PutAsync(new BackupObjectRef("2025/Doc.pdf", null), Content("payload"), "application/pdf", ct);

        await _sut.DeleteAsync(new BackupObjectRef("2025/Doc.pdf", null), ct);

        Assert.False(Directory.Exists(Absolute("2025")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task DeleteAsync_KeepsAShardThatStillHasFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.PutAsync(new BackupObjectRef("2025/A.pdf", null), Content("a"), "application/pdf", ct);
        await _sut.PutAsync(new BackupObjectRef("2025/B.pdf", null), Content("b"), "application/pdf", ct);

        await _sut.DeleteAsync(new BackupObjectRef("2025/A.pdf", null), ct);

        Assert.True(Directory.Exists(Absolute("2025")));
        Assert.True(File.Exists(Absolute("2025/B.pdf")));
    }

    // The last gate before a write lands on a real filesystem. The planner sanitizes every segment, so
    // this should be unreachable — which is exactly why it is asserted rather than assumed.
    [Theory]
    [InlineData("../escaped.pdf")]
    [InlineData("2025/../../escaped.pdf")]
    [InlineData("2025/../../../etc/passwd")]
    public async Task PutAsync_RefusesAPathThatEscapesTheRoot(string relativePath)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PutAsync(
            new BackupObjectRef(relativePath, null), Content("nope"), "application/pdf",
            TestContext.Current.CancellationToken));

        Assert.Contains("escapes the target root", ex.Message);
    }

    [Fact]
    public async Task PutAsync_RefusesAnAbsolutePath()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PutAsync(
            new BackupObjectRef("/etc/passwd", null), Content("nope"), "application/pdf",
            TestContext.Current.CancellationToken));

        Assert.Contains("must be relative", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_RefusesAPathThatEscapesTheRoot()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteAsync(
            new BackupObjectRef("../escaped.pdf", null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveAsync_RefusesADestinationThatEscapesTheRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.PutAsync(new BackupObjectRef("2025/Doc.pdf", null), Content("payload"), "application/pdf", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.MoveAsync(
            new BackupObjectRef("2025/Doc.pdf", null), "../escaped.pdf", ct));
    }

    [Fact]
    public async Task AnUnavailableTarget_RefusesEveryWrite()
    {
        var unavailable = new LocalDirectoryBackupTarget(
            "broken",
            new BackupTargetOptions { Kind = BackupTargetKind.Local, Path = "  " },
            new FileStorageOptions());

        Assert.False(unavailable.IsAvailable);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.PutAsync(
            new BackupObjectRef("2025/Doc.pdf", null), Content("nope"), "application/pdf",
            TestContext.Current.CancellationToken));
    }
}
