using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentFile;

public class GetDocumentFileHandler(AppDbContext db, IConfiguration configuration, IDocumentAccessService accessService)
    : IQueryHandler<GetDocumentFileRequest, IResult>
{
    public async Task<IResult> HandleAsync(GetDocumentFileRequest request, CancellationToken ct)
    {
        var access = await accessService.CheckAccessAsync(request.Id, DocumentOperation.Read, ct);
        if (!access.IsAllowed)
            return Results.Forbid();

        var documentName = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct);

        if (documentName is null)
            return Results.NotFound();

        var versionQuery = db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.Id);

        if (request.VersionId.HasValue)
            versionQuery = versionQuery.Where(r => r.Id == request.VersionId.Value);
        else
            versionQuery = versionQuery.OrderByDescending(r => r.VersionNumber);

        var version = await versionQuery.FirstOrDefaultAsync(ct);

        if (version is null)
            return Results.NotFound();

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = version.GetPhysicalPath(mediaPath);

        if (!File.Exists(physicalPath))
            return Results.NotFound();

        return request.Download == true
            ? Results.File(physicalPath, version.ContentType, version.GetDownloadFileName(documentName))
            : Results.File(physicalPath, version.ContentType);
    }
}
