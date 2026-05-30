using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

internal static class DocumentPermanentDeleter
{
    public static async Task<int> ExecuteAsync(
        List<Document> documents, AppDbContext db, IConfiguration configuration,
        IAuditContext audit, IUnitOfWork unitOfWork, ILogger logger, CancellationToken ct)
    {
        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var versions = documents.SelectMany(d => d.Versions).ToList();
        var physicalPaths = versions
            .Select(r => r.GetPhysicalPath(mediaPath))
            .ToList();

        // Detach the loaded versions so EF deletes only the parent documents; the DB-level
        // ON DELETE CASCADE then removes their versions parent-first. This keeps the
        // document_versions search-vector trigger from recomputing the parent's vector once per
        // version of a row that is about to be deleted (the parent is already gone when the
        // cascaded version deletes fire). Other dependents (tags/parties/comments/suggestions) are
        // untracked already and cascade the same way.
        foreach (var version in versions)
            db.Entry(version).State = EntityState.Detached;

        foreach (var doc in documents)
        {
            audit.Deleted("Document", doc.Id, description: doc.Name);
        }

        db.Documents.RemoveRange(documents);
        await unitOfWork.SaveChangesAsync(ct);

        // Delete the backing files only after the row removals have durably committed.
        unitOfWork.OnAfterCommit(_ =>
        {
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
            return Task.CompletedTask;
        });

        return documents.Count;
    }
}
