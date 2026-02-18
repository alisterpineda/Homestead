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

        var basePath = configuration["FileStorage:BasePath"]
            ?? throw new InvalidOperationException("FileStorage:BasePath is not configured.");
        var filePath = Path.Combine(basePath, document.FilePath);

        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);

        if (File.Exists(filePath))
            File.Delete(filePath);

        return new DeleteDocumentResponse();
    }
}
