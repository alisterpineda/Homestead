using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public class BulkDeleteDocumentsHandler(AppDbContext db, IConfiguration configuration, ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger)
    : ICommandHandler<BulkDeleteDocumentsRequest, BulkDeleteDocumentsResponse>
{
    public async Task<BulkDeleteDocumentsResponse> HandleAsync(BulkDeleteDocumentsRequest request, CancellationToken ct)
    {
        var documents = await db.Documents
            .Include(d => d.Versions)
            .Where(d => request.Ids.Contains(d.Id))
            .ToListAsync(ct);

        if (documents.Count == 0)
            return new BulkDeleteDocumentsResponse(0);

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPaths = documents
            .SelectMany(d => d.Versions)
            .Select(r => r.GetPhysicalPath(mediaPath))
            .ToList();

        foreach (var doc in documents)
        {
            auditLogger.LogDeleted("Document", doc.Id, currentUser.UserId, description: doc.Name);
        }

        db.Documents.RemoveRange(documents);
        await db.SaveChangesAsync(ct);

        foreach (var path in physicalPaths)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        return new BulkDeleteDocumentsResponse(documents.Count);
    }
}
