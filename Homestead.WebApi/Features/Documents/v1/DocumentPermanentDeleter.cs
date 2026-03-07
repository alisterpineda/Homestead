using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Features.Documents.v1;

internal static class DocumentPermanentDeleter
{
    public static async Task<int> ExecuteAsync(
        List<Document> documents, AppDbContext db, IConfiguration configuration,
        ICurrentUserAccessor currentUser, IAuditLogger auditLogger, CancellationToken ct)
    {
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
            File.Delete(path);
        }

        return documents.Count;
    }
}
