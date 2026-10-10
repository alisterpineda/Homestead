using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Documents.Processing.Commands;

/// <summary>
/// Internal command (no HTTP surface, no document-authorization marker — authorization happened at
/// upload enqueue) that persists a freshly-uploaded document and its first version in one unit of work.
/// When AI classification ran in-memory during the job, its result is folded in here so the document is
/// created with the AI name/tags/parties/summary already applied — yielding a single <c>Created</c> audit
/// entry rather than a Created followed by a suppressed Updated.
/// </summary>
public record CreateDocumentFromUploadCommand
{
    public required string Name { get; init; }
    public Guid? GroupId { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required string FileExtension { get; init; }
    public string? Label { get; init; }
    public string? ExtractedText { get; init; }

    /// <summary>Serialized TextExtractionMetadata describing how ExtractedText was produced.</summary>
    public string? TextExtractionMetadata { get; init; }

    /// <summary>Optional AI classification produced in-memory during the job (null when auto-classify is off).</summary>
    public ClassificationResult? Classification { get; init; }

    /// <summary>The uploading user: becomes the document's creator and owner. Audit attribution is set separately on the unit of work.</summary>
    public Guid? ActorUserId { get; init; }
}

public record CreateDocumentFromUploadResult(Guid DocumentId, string Name);

public interface ICreateDocumentFromUploadHandler
{
    Task<CreateDocumentFromUploadResult> Handle(CreateDocumentFromUploadCommand request, CancellationToken ct);
}

public class CreateDocumentFromUploadHandler(
    AppDbContext db, IDocumentClassifier classifier, IAuditContext audit) : ICreateDocumentFromUploadHandler
{
    public async Task<CreateDocumentFromUploadResult> Handle(CreateDocumentFromUploadCommand request, CancellationToken ct)
    {
        var document = new Document
        {
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = request.ActorUserId,
            OwnerUserId = request.ActorUserId,
            GroupId = request.GroupId
        };

        var version = new DocumentVersion
        {
            VersionNumber = 1,
            Label = request.Label,
            ContentType = request.ContentType,
            FileSize = request.FileSize,
            FilePath = DocumentVersion.GetFilePath(request.FileHash, request.FileExtension),
            FileHash = request.FileHash,
            FileExtension = request.FileExtension,
            CreatedAt = DateTimeOffset.UtcNow,
            Document = document,
            ExtractedText = request.ExtractedText,
            TextExtractionMetadata = request.TextExtractionMetadata
        };

        db.Documents.Add(document);
        db.DocumentVersions.Add(version);

        // Fold the in-memory classification into creation: applied as part of the initial state, so no
        // modified-metadata stamp (stampSummaryMetadata: false) — the document reads as created-only.
        if (request.Classification is not null)
        {
            await classifier.ApplyAsync(document, request.Classification,
                ClassificationScope.All, request.ActorUserId, stampSummaryMetadata: false, ct);
        }

        audit.Created("Document", document.Id);

        // The job handler's unit of work commits once after this handler returns — a single Created row.
        // SearchVector is maintained by DB triggers as document + version are inserted.
        return new CreateDocumentFromUploadResult(document.Id, document.Name);
    }
}
