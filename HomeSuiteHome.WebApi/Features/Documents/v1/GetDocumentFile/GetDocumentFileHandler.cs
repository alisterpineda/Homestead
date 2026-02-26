using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentFile;

public class GetDocumentFileHandler(AppDbContext db, IConfiguration configuration)
    : IQueryHandler<GetDocumentFileRequest, GetDocumentFileResponse?>
{
    public async Task<GetDocumentFileResponse?> HandleAsync(GetDocumentFileRequest request, CancellationToken ct)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return null;

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = document.GetPhysicalPath(mediaPath);

        if (!File.Exists(physicalPath))
            return null;

        return new GetDocumentFileResponse(physicalPath, document.ContentType, document.GetDownloadFileName());
    }
}
