using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentFile;

public class GetDocumentFileHandler(AppDbContext db, IConfiguration configuration)
    : IRequestHandler<GetDocumentFileRequest, IResult>
{
    public async Task<IResult> Handle(GetDocumentFileRequest request, CancellationToken ct)
    {
        var documentName = await db.Documents
            .IgnoreQueryFilters()
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
