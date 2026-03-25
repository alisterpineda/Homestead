using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Features.Documents;

internal static class DocumentPermanentDeleter
{
    public static async Task<int> ExecuteAsync(
        List<Document> documents, AppDbContext db, IConfiguration configuration,
        IAuditLogger auditLogger, ILogger logger, CancellationToken ct)
    {
        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPaths = documents
            .SelectMany(d => d.Versions)
            .Select(r => r.GetPhysicalPath(mediaPath))
            .ToList();

        foreach (var doc in documents)
        {
            auditLogger.LogDeleted("Document", doc.Id, description: doc.Name);
        }

        db.Documents.RemoveRange(documents);
        await db.SaveChangesAsync(ct);

        foreach (var path in physicalPaths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete file {Path}", path);
            }
        }

        return documents.Count;
    }
}
