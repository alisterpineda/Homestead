using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Features.Documents.Thumbnails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentThumbnail;

public class GetDocumentThumbnailHandler(
    AppDbContext db,
    IConfiguration configuration,
    IAuthorizationService authService,
    IHttpContextAccessor httpContext,
    ThumbnailService thumbnailService)
    : IRequestHandler<GetDocumentThumbnailRequest, IResult>
{
    public async Task<IResult> Handle(GetDocumentThumbnailRequest request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.Id, DocumentOperations.Read);

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
        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var sourcePath = Path.Combine(mediaPath, version.FilePath);

        var generatedPath = await thumbnailService.GenerateAsync(
            sourcePath, version.ContentType, version.FileHash, ct);

        if (generatedPath is null)
            return Results.NotFound();

        return Results.File(generatedPath, "image/webp");
    }
}
