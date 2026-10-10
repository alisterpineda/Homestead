using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentFile;

public class GetDocumentFileHandler(AppDbContext db, IOptions<FileStorageOptions> storage)
{
    public async Task<IResult> Handle(GetDocumentFileQuery request, CancellationToken ct)
    {
        var documentName = await db.Documents
            .IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
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

        var mediaPath = storage.Value.MediaPath;

        var physicalPath = version.GetPhysicalPath(mediaPath);

        if (!File.Exists(physicalPath))
            return Results.NotFound();

        return request.Download == true
            ? Results.File(physicalPath, version.ContentType, version.GetDownloadFileName(documentName))
            : Results.File(physicalPath, version.ContentType);
    }
}
