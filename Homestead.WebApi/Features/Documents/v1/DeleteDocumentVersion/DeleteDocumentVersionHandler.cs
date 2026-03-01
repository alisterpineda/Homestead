using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public class DeleteDocumentVersionHandler(AppDbContext db, IConfiguration configuration, ICurrentUserAccessor currentUser)
    : ICommandHandler<DeleteDocumentVersionRequest, IResult>
{
    public async Task<IResult> HandleAsync(DeleteDocumentVersionRequest request, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return Results.NotFound();

        // Prevent deleting the last version
        var versionCount = await db.DocumentVersions
            .CountAsync(r => r.DocumentId == request.DocumentId, ct);

        if (versionCount <= 1)
            return Results.BadRequest("Cannot delete the last version of a document.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = version.GetPhysicalPath(mediaPath);

        var documentId = version.DocumentId;

        var description = $"Version {version.VersionNumber}";
        if (!string.IsNullOrWhiteSpace(version.Label))
            description += $" ({version.Label})";

        AuditLogger.LogDeleted(db, "DocumentVersion", version.Id, currentUser.UserId,
            description: description, parentEntityId: documentId);

        db.DocumentVersions.Remove(version);
        await db.SaveChangesAsync(ct);

        await SearchVectorHelper.UpdateSearchVectorAsync(db, documentId, ct);

        if (File.Exists(physicalPath))
            File.Delete(physicalPath);

        return Results.NoContent();
    }
}
