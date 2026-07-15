using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing.Commands;

/// <summary>
/// Internal command that appends a new version to an existing document in one unit of work. Distinct
/// from the HTTP <c>CreateDocumentVersionCommand</c>, which stages the file and enqueues the job that
/// ultimately sends this. No document-authorization marker — authorization happened at enqueue.
/// </summary>
public record AddDocumentVersionCommand : ICommand<AddDocumentVersionResult>, IActorAttributed
{
    public required Guid DocumentId { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required string FileExtension { get; init; }
    public string? Label { get; init; }
    public string? ExtractedText { get; init; }

    /// <summary>Serialized TextExtractionMetadata describing how ExtractedText was produced.</summary>
    public string? TextExtractionMetadata { get; init; }

    public Guid? ActorUserId { get; init; }
}

public record AddDocumentVersionResult(Guid VersionId, string DocumentName);

public class AddDocumentVersionHandler(AppDbContext db, IAuditContext audit)
    : IRequestHandler<AddDocumentVersionCommand, AddDocumentVersionResult>
{
    public async Task<AddDocumentVersionResult> Handle(AddDocumentVersionCommand request, CancellationToken ct)
    {
        var documentName = await db.Documents
            .Where(d => d.Id == request.DocumentId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Document {request.DocumentId} not found.");

        var maxVersionNumber = await db.DocumentVersions
            .Where(r => r.DocumentId == request.DocumentId)
            .MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0;

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = maxVersionNumber + 1,
            Label = request.Label,
            ContentType = request.ContentType,
            FileSize = request.FileSize,
            FilePath = DocumentVersion.GetFilePath(request.FileHash, request.FileExtension),
            FileHash = request.FileHash,
            FileExtension = request.FileExtension,
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = request.DocumentId,
            ExtractedText = request.ExtractedText,
            TextExtractionMetadata = request.TextExtractionMetadata
        };

        db.DocumentVersions.Add(version);
        audit.Created("DocumentVersion", version.Id, parentEntityId: request.DocumentId);

        // The UnitOfWorkBehavior commits; the DB trigger refreshes the parent document's SearchVector
        // with the new latest version's extracted text.
        return new AddDocumentVersionResult(version.Id, documentName);
    }
}
