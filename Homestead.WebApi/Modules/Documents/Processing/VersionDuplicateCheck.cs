using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

/// <summary>
/// The shared duplicate-content probe for upload processing: finds any existing version with the
/// given file hash across all documents globally. The soft-delete filter is lifted so trashed
/// documents' versions are still detected — matching the unique index IX_document_versions_FileHash,
/// which spans them too.
/// </summary>
public static class VersionDuplicateCheck
{
    public sealed record Match(int VersionNumber, Guid DocumentId, string DocumentName, DateTimeOffset? DeletedAt)
    {
        public bool IsTrashed => DeletedAt != null;
    }

    public static Task<Match?> FindByHashAsync(AppDbContext db, string fileHash, CancellationToken ct) =>
        db.DocumentVersions
            .IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .Where(v => v.FileHash == fileHash)
            .Select(v => new Match(v.VersionNumber, v.Document.Id, v.Document.Name, v.Document.DeletedAt))
            .FirstOrDefaultAsync(ct);
}
