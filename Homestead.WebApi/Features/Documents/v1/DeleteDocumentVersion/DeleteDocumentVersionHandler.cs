using Homestead.WebApi.Common;
using Homestead.WebApi.Features.Auditing;
using MediatR;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public class DeleteDocumentVersionHandler(AppDbContext db, IConfiguration configuration,
    IAuditContext audit, ISearchVectorUpdater searchVectorUpdater,
    ILogger<DeleteDocumentVersionHandler> logger)
    : IRequestHandler<DeleteDocumentVersionCommand, bool>
{
    public async Task<bool> Handle(DeleteDocumentVersionCommand request, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            throw new EntityNotFoundException("Document version not found.");

        // Prevent deleting the last version
        var versionCount = await db.DocumentVersions
            .CountAsync(r => r.DocumentId == request.DocumentId, ct);

        if (versionCount <= 1)
            throw new BusinessRuleException("Cannot delete the last version of a document.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = version.GetPhysicalPath(mediaPath);

        var documentId = version.DocumentId;

        var description = $"Version {version.VersionNumber}";
        if (!string.IsNullOrWhiteSpace(version.Label))
            description += $" ({version.Label})";

        audit.Deleted("DocumentVersion", version.Id,
            description: description, parentEntityId: documentId);

        db.DocumentVersions.Remove(version);
        await db.SaveChangesAsync(ct);

        await searchVectorUpdater.UpdateSearchVectorAsync(documentId, ct);

        try
        {
            File.Delete(physicalPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete file {Path}", physicalPath);
        }

        return true;
    }
}
