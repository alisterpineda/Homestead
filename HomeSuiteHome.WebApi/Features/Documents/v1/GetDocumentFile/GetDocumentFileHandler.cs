using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentFile;

public class GetDocumentFileHandler(AppDbContext db, IConfiguration configuration)
    : IQueryHandler<GetDocumentFileRequest, GetDocumentFileResponse?>
{
    public async Task<GetDocumentFileResponse?> HandleAsync(GetDocumentFileRequest request, CancellationToken ct)
    {
        var documentName = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct);

        if (documentName is null)
            return null;

        var revisionQuery = db.DocumentRevisions
            .AsNoTracking()
            .Where(r => r.DocumentId == request.Id);

        if (request.RevisionId.HasValue)
            revisionQuery = revisionQuery.Where(r => r.Id == request.RevisionId.Value);
        else
            revisionQuery = revisionQuery.OrderByDescending(r => r.RevisionNumber);

        var revision = await revisionQuery.FirstOrDefaultAsync(ct);

        if (revision is null)
            return null;

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = revision.GetPhysicalPath(mediaPath);

        if (!File.Exists(physicalPath))
            return null;

        return new GetDocumentFileResponse(physicalPath, revision.ContentType, revision.GetDownloadFileName(documentName));
    }
}
