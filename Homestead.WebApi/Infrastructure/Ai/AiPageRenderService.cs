using Homestead.WebApi.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.Ai;

// Generate-on-demand + cache for the dedicated AI page render, mirroring ThumbnailService. The cache path
// is derived from the version's FileHash + the render edge (no DB column, no migration) and the render is
// an internal asset (no serving endpoint). GetOrCreateAsync is virtual so unit tests can substitute it.
public class AiPageRenderService(
    IDocumentPageRenderer renderer,
    IOptions<FileStorageOptions> storage,
    ILogger<AiPageRenderService> logger)
{
    private readonly string _cachePath = storage.Value.CachePath;

    // {CachePath}/ai-renders/{hash[0:4]}/{hash}-{maxEdge}.png — sibling of GetThumbnailPath, hash-sharded
    // the same way. The edge is part of the key: providers can carry different ImageRenderMaxEdge values,
    // and a hash-only key would hand one provider's cached resolution to another (and would keep serving a
    // stale resolution after an operator changes the edge config).
    public string GetRenderPath(string fileHash, int maxEdge)
    {
        var prefix = fileHash[..4];
        return Path.Combine(_cachePath, "ai-renders", prefix, $"{fileHash}-{maxEdge}.png");
    }

    // Returns the cached render bytes if present, else renders page 1 at the given long-edge budget, writes
    // the cache, and returns the bytes. Callers supply maxEdge from their resolved provider's
    // ImageRenderMaxEdge (a parameter, like the renderer itself — injecting the resolver here would
    // hard-wire this service to the default provider). Returns null for an unsupported content type or a
    // render failure -> caller classifies on text alone. Never throws on a render failure (best-effort,
    // like thumbnails).
    public virtual async Task<byte[]?> GetOrCreateAsync(
        string fileHash, string sourcePath, string contentType, int maxEdge, CancellationToken ct)
    {
        var outputPath = GetRenderPath(fileHash, maxEdge);

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
            var bytes = await renderer.RenderFirstPagePngAsync(sourcePath, contentType, maxEdge, ct);
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
