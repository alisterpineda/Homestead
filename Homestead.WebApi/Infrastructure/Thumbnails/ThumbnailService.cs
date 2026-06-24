using Homestead.WebApi.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.Thumbnails;

public class ThumbnailService
{
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/webp",
        "application/pdf"
    };

    private readonly IEnumerable<IThumbnailGenerator> _generators;
    private readonly string _cachePath;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(
        IEnumerable<IThumbnailGenerator> generators,
        IOptions<FileStorageOptions> storage,
        ILogger<ThumbnailService> logger)
    {
        _generators = generators;
        _cachePath = storage.Value.CachePath;
        _logger = logger;
    }

    /// <summary>
    /// Static check used in EF LINQ projections (GetDocumentsHandler) where an instance is unavailable.
    /// Must be kept in sync with registered IThumbnailGenerator implementations.
    /// </summary>
    public static bool CanGenerate(string contentType) => SupportedContentTypes.Contains(contentType);

    public bool ThumbnailExists(string fileHash) => File.Exists(GetThumbnailPath(fileHash));

    public string GetThumbnailPath(string fileHash)
    {
        var prefix = fileHash[..4];
        return Path.Combine(_cachePath, "thumbnails", prefix, $"{fileHash}.webp");
    }

    public async Task<string?> GenerateAsync(string sourcePath, string contentType, string fileHash, CancellationToken ct)
    {
        var outputPath = GetThumbnailPath(fileHash);

        if (File.Exists(outputPath))
            return outputPath;

        var generator = _generators.FirstOrDefault(g => g.CanGenerate(contentType));
        if (generator is null)
            return null;

        try
        {
            var success = await generator.GenerateAsync(sourcePath, outputPath, ct);
            return success ? outputPath : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail generation failed for {FileHash} ({ContentType})", fileHash, contentType);
            // Clean up partial file
            try { File.Delete(outputPath); } catch { /* best effort */ }
            return null;
        }
    }
}
