using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteDocumentRevision;

public class DeleteDocumentRevisionHandler(AppDbContext db, IConfiguration configuration)
    : ICommandHandler<DeleteDocumentRevisionRequest, IResult>
{
    public async Task<IResult> HandleAsync(DeleteDocumentRevisionRequest request, CancellationToken ct)
    {
        var revision = await db.DocumentRevisions
            .Where(r => r.DocumentId == request.DocumentId && r.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (revision is null)
            return Results.NotFound();

        // Prevent deleting the last revision
        var revisionCount = await db.DocumentRevisions
            .CountAsync(r => r.DocumentId == request.DocumentId, ct);

        if (revisionCount <= 1)
            return Results.BadRequest("Cannot delete the last revision of a document.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPath = revision.GetPhysicalPath(mediaPath);

        db.DocumentRevisions.Remove(revision);
        await db.SaveChangesAsync(ct);

        if (File.Exists(physicalPath))
            File.Delete(physicalPath);

        return Results.NoContent();
    }
}
