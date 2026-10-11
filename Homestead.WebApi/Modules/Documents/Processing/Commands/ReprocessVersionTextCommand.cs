using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing.Commands;

/// <summary>
/// Internal command that writes freshly re-extracted text onto an existing version. The job handler
/// performs the (failure-prone) text extraction; this command only persists the result and audits the
/// change. Distinct from the HTTP <c>ReprocessDocumentVersionHandler</c> (which enqueues the job).
/// </summary>
public record ReprocessVersionTextCommand
{
    public required Guid DocumentId { get; init; }
    public required Guid VersionId { get; init; }
    public required string? ExtractedText { get; init; }

    /// <summary>Serialized TextExtractionMetadata describing how ExtractedText was produced.</summary>
    public required string? TextExtractionMetadata { get; init; }
}

public record ReprocessVersionTextResult(string DocumentName);

public interface IReprocessVersionTextHandler
{
    Task<ReprocessVersionTextResult> HandleAsync(ReprocessVersionTextCommand request, CancellationToken ct);
}

public class ReprocessVersionTextHandler(AppDbContext db, IAuditContext audit) : IReprocessVersionTextHandler
{
    public async Task<ReprocessVersionTextResult> HandleAsync(ReprocessVersionTextCommand request, CancellationToken ct)
    {
        var version = await db.DocumentVersions
            .Include(v => v.Document)
            .FirstOrDefaultAsync(v => v.Id == request.VersionId && v.DocumentId == request.DocumentId, ct)
            ?? throw new InvalidOperationException($"DocumentVersion {request.VersionId} not found.");

        // Track before mutating; an unchanged ExtractedText yields an empty diff and no audit row.
        audit.Track<DocumentVersionSnapshot, DocumentVersion>(version, "DocumentVersion", version.Id,
            parentEntityId: version.DocumentId);

        version.ExtractedText = request.ExtractedText;
        version.TextExtractionMetadata = request.TextExtractionMetadata;

        // The job handler's unit of work commits; the DB trigger refreshes the document's SearchVector.
        return new ReprocessVersionTextResult(version.Document.Name);
    }
}
