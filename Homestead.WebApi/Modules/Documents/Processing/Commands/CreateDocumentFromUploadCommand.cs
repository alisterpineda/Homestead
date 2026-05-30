using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Processing.Commands;

/// <summary>
/// Internal command (no HTTP surface, no document-authorization marker — authorization happened at
/// upload enqueue) that persists a freshly-uploaded document and its first version in one unit of work.
/// When AI classification ran in-memory during the job, its result is folded in here so the document is
/// created with the AI name/tags/parties/summary already applied — yielding a single <c>Created</c> audit
/// entry rather than a Created followed by a suppressed Updated.
/// </summary>
public record CreateDocumentFromUploadCommand : ICommand<CreateDocumentFromUploadResult>, IActorAttributed
{
    public required string Name { get; init; }
    public Guid? GroupId { get; init; }
    public required string ContentType { get; init; }
    public required long FileSize { get; init; }
    public required string FileHash { get; init; }
    public required string FileExtension { get; init; }
    public string? Label { get; init; }
    public string? ExtractedText { get; init; }

    /// <summary>Optional AI classification produced in-memory during the job (null when auto-classify is off).</summary>
    public ClassificationResult? Classification { get; init; }
    public Guid? ProviderId { get; init; }

    public Guid? ActorUserId { get; init; }
}

public record CreateDocumentFromUploadResult(Guid DocumentId, string Name);

public class CreateDocumentFromUploadHandler(
    AppDbContext db, IDocumentClassifier classifier, IAuditContext audit)
    : IRequestHandler<CreateDocumentFromUploadCommand, CreateDocumentFromUploadResult>
{
    public async Task<CreateDocumentFromUploadResult> Handle(CreateDocumentFromUploadCommand request, CancellationToken ct)
    {
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            Id = documentId,
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = request.ActorUserId,
            OwnerUserId = request.ActorUserId,
            GroupId = request.GroupId
        };

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = 1,
            Label = request.Label,
            ContentType = request.ContentType,
            FileSize = request.FileSize,
            FilePath = DocumentVersion.GetFilePath(request.FileHash, request.FileExtension),
            FileHash = request.FileHash,
            FileExtension = request.FileExtension,
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = documentId,
            ExtractedText = request.ExtractedText
        };

        db.Documents.Add(document);
        db.DocumentVersions.Add(version);

        // Fold the in-memory classification into creation: applied as part of the initial state, so no
        // modified-metadata stamp (stampSummaryMetadata: false) — the document reads as created-only.
        if (request.Classification is not null && request.ProviderId is not null)
        {
            await classifier.ApplyAsync(document, request.Classification, request.ProviderId.Value,
                ClassificationScope.All, request.ActorUserId, stampSummaryMetadata: false, ct);
        }

        audit.Created("Document", documentId);

        // The UnitOfWorkBehavior commits once after this handler returns — a single Created row.
        // SearchVector is maintained by DB triggers as document + version are inserted.
        return new CreateDocumentFromUploadResult(documentId, document.Name);
    }
}
