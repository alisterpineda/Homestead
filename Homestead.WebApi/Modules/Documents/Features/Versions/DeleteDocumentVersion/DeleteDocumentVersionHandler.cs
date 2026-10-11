using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.DeleteDocumentVersion;

public class DeleteDocumentVersionHandler(AppDbContext db, IOptions<FileStorageOptions> storage,
    IAuditContext audit, IUnitOfWork unitOfWork,
    ILogger<DeleteDocumentVersionHandler> logger)
{
    public async Task<bool> HandleAsync(Guid documentId, Guid id, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == documentId && r.Id == id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            throw new EntityNotFoundException("Document version not found.");

        // Prevent deleting the last version
        var versionCount = await db.DocumentVersions
            .CountAsync(r => r.DocumentId == documentId, ct);

        if (versionCount <= 1)
            throw new BusinessRuleException("Cannot delete the last version of a document.");

        var mediaPath = storage.Value.MediaPath;

        var physicalPath = version.GetPhysicalPath(mediaPath);

        var description = $"Version {version.VersionNumber}";
        if (!string.IsNullOrWhiteSpace(version.Label))
            description += $" ({version.Label})";

        audit.Deleted("DocumentVersion", version.Id,
            description: description, parentEntityId: documentId);

        db.DocumentVersions.Remove(version);
        await unitOfWork.SaveChangesAsync(ct);

        // Delete the backing file only after the row removal has durably committed.
        unitOfWork.OnAfterCommit(_ =>
        {
            try
            {
                File.Delete(physicalPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete file {Path}", physicalPath);
            }
            return Task.CompletedTask;
        });

        return true;
    }
}
