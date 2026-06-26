using Homestead.WebApi.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.Ai;

// Generate-on-demand + cache for the dedicated AI page render, mirroring ThumbnailService. The cache path
// is derived from the version's FileHash (no DB column, no migration) and the render is an internal asset
// (no serving endpoint). GetOrCreateAsync is virtual so unit tests can substitute it.
public class AiPageRenderService(
    IDocumentPageRenderer renderer,
    IOptions<FileStorageOptions> storage,
    IOptions<AiOptions> aiOptions,
    ILogger<AiPageRenderService> logger)
{
    private readonly string _cachePath = storage.Value.CachePath;
    private readonly AiOptions _aiOptions = aiOptions.Value;

    // {CachePath}/ai-renders/{hash[0:4]}/{hash}.png — sibling of GetThumbnailPath, hash-sharded the same way.
    public string GetRenderPath(string fileHash)
    {
        var prefix = fileHash[..4];
        return Path.Combine(_cachePath, "ai-renders", prefix, $"{fileHash}.png");
    }

    // Returns the cached render bytes if present, else renders page 1, writes the cache, and returns the
    // bytes. Returns null for an unsupported content type or a render failure -> caller classifies on text
    // alone. Never throws on a render failure (best-effort, like thumbnails).
    public virtual async Task<byte[]?> GetOrCreateAsync(
        string fileHash, string sourcePath, string contentType, CancellationToken ct)
    {
        var outputPath = GetRenderPath(fileHash);

        if (File.Exists(outputPath))
        {
            try
            {
                return await File.ReadAllBytesAsync(outputPath, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read cached AI render {FileHash}; re-rendering", fileHash);
            }
        }

        if (!renderer.CanRender(contentType))
            return null;

        try
        {
            var bytes = await renderer.RenderFirstPagePngAsync(
                sourcePath, contentType, _aiOptions.ResolveImageRenderMaxEdge(), ct);
            if (bytes is null)
                return null;

            var dir = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(dir);
            // Write to a unique temp then atomically move into place. The cache path therefore only ever
            // holds a complete file: a cancelled/partial write can't leave a torn PNG that File.Exists would
            // later serve, and two concurrent renders of the same content hash each write their own temp and
            // can't corrupt each other (last move wins; both produced identical bytes).
            var tempPath = Path.Combine(dir, $"{fileHash}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, ct);
                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
                throw;
            }
            return bytes;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI page render failed for {FileHash} ({ContentType})", fileHash, contentType);
            return null;
        }
    }
}
