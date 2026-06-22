using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing.Commands;

/// <summary>
/// Internal command that applies a <em>precomputed</em> classification to an existing document as a net
/// diff — a genuine <c>Updated</c>. The slow AI call ran upstream in the job scope
/// (<see cref="ClassifyDocumentJobHandler"/>), so this command does only the thin persist and holds the
/// command transaction for the minimum time. No document-authorization marker — authorization happened
/// at enqueue.
/// </summary>
public record ApplyClassificationCommand : ICommand<ApplyClassificationResult>, IActorAttributed
{
    public required Guid DocumentId { get; init; }

    /// <summary>The classification produced upstream in the job scope; null when AI is unconfigured or the model produced nothing.</summary>
    public ClassificationResult? Classification { get; init; }

    public ClassificationScope Scope { get; init; } = ClassificationScope.All;
    public Guid? ActorUserId { get; init; }
}

public record ApplyClassificationResult(string DocumentName);

public class ApplyClassificationHandler(
    AppDbContext db, IDocumentClassifier classifier, IAuditContext audit)
    : IRequestHandler<ApplyClassificationCommand, ApplyClassificationResult>
{
    public async Task<ApplyClassificationResult> Handle(ApplyClassificationCommand request, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new InvalidOperationException($"Document {request.DocumentId} not found.");

        // null = AI unconfigured / nothing produced upstream → graceful no-op (no audit, no changes).
        if (request.Classification is not null)
        {
            // Track before applying so the in-memory snapshot diff (now resolving Tag.Path from the
            // .Tag nav set in ApplyAsync) becomes the Updated audit entry at the chokepoint flush.
            audit.Track<DocumentSnapshot, Document>(document, "Document", document.Id);

            await classifier.ApplyAsync(document, request.Classification, request.Scope,
                request.ActorUserId, stampSummaryMetadata: true, ct);
        }

        return new ApplyClassificationResult(document.Name);
    }
}
