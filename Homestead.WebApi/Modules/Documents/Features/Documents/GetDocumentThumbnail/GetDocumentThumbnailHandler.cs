using Homestead.WebApi.Persistence;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentThumbnail;

public class GetDocumentThumbnailHandler(
    AppDbContext db,
    IOptions<FileStorageOptions> storage,
    ThumbnailService thumbnailService)
{
    public async Task<IResult> HandleAsync(Guid id, Guid? versionId, CancellationToken ct)
    {
        var versionQuery = db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == id);

        if (versionId.HasValue)
            versionQuery = versionQuery.Where(r => r.Id == versionId.Value);
        else
            versionQuery = versionQuery.OrderByDescending(r => r.VersionNumber);

        var version = await versionQuery
            .Select(r => new { r.ContentType, r.FileHash, r.FilePath })
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return Results.NotFound();

        if (!ThumbnailService.CanGenerate(version.ContentType))
            return Results.NotFound();

        // Check cache first (compute path once)
        var thumbnailPath = thumbnailService.GetThumbnailPath(version.FileHash);
        if (File.Exists(thumbnailPath))
            return Results.File(thumbnailPath, "image/webp");

        // On-demand generation
        var mediaPath = storage.Value.MediaPath;

        var sourcePath = Path.Combine(mediaPath, version.FilePath);

        var generatedPath = await thumbnailService.GenerateAsync(
            sourcePath, version.ContentType, version.FileHash, ct);

        if (generatedPath is null)
            return Results.NotFound();

        return Results.File(generatedPath, "image/webp");
    }
}
