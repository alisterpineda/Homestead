using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteDocument;

public class DeleteDocumentHandler(AppDbContext db, IConfiguration configuration)
    : ICommandHandler<DeleteDocumentRequest, DeleteDocumentResponse?>
{
    public async Task<DeleteDocumentResponse?> HandleAsync(DeleteDocumentRequest request, CancellationToken ct)
    {
        var document = await db.Documents.FindAsync([request.Id], ct);
        if (document is null)
            return null;

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");
        var physicalPath = document.GetPhysicalPath(mediaPath);

        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);

        if (File.Exists(physicalPath))
            File.Delete(physicalPath);

        return new DeleteDocumentResponse();
    }
}
