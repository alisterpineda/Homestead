using MediatR;
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
    : IRequestHandler<GetDocumentThumbnailQuery, IResult>
{
    public async Task<IResult> Handle(GetDocumentThumbnailQuery request, CancellationToken ct)
    {
        var versionQuery = db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.Id);

        if (request.VersionId.HasValue)
            versionQuery = versionQuery.Where(r => r.Id == request.VersionId.Value);
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
