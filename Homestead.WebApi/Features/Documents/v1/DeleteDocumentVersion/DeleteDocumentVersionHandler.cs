using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public class DeleteDocumentVersionHandler(AppDbContext db, IConfiguration configuration,
    IAuditLogger auditLogger, ISearchVectorUpdater searchVectorUpdater,
    IAuthorizationService authService, IHttpContextAccessor httpContext,
    ILogger<DeleteDocumentVersionHandler> logger)
    : ICommandHandler<DeleteDocumentVersionRequest, bool>
{
    public async Task<bool> HandleAsync(DeleteDocumentVersionRequest request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.DocumentId, DocumentOperations.Write);

        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            throw new NotFoundException("Document version not found.");

        // Prevent deleting the last version
        var versionCount = await db.DocumentVersions
            .CountAsync(r => r.DocumentId == request.DocumentId, ct);

        if (versionCount <= 1)
            throw new BadRequestException("Cannot delete the last version of a document.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = version.GetPhysicalPath(mediaPath);

        var documentId = version.DocumentId;

        var description = $"Version {version.VersionNumber}";
        if (!string.IsNullOrWhiteSpace(version.Label))
            description += $" ({version.Label})";

        auditLogger.LogDeleted("DocumentVersion", version.Id,
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
