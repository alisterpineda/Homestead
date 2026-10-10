using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;

public class SummarizeDocumentHandler(
    AppDbContext db,
    IDocumentClassifier classifier,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser)
{
    public async Task<SummarizeDocumentResponse> Handle(SummarizeDocumentCommand request, CancellationToken ct)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Document not found.");

        // Track for the activity stream (matches PatchDocument); flushed when CommandTransactionFilter commits.
        audit.Track<DocumentSnapshot, Document>(document, "Document", document.Id);

        // This summarize command is synchronous; never attach the page image (image-carrying classification
        // must run on the background-job path, and this is the handler the slow-work architecture test
        // explicitly allowlists). Stays text-only.
        var output = await classifier.ClassifyAsync(
            request.DocumentId, document.Name, ClassificationScope.Summary, ct, attachImage: false);

        // null = no provider / nothing to summarize; blank = the model produced no usable summary.
        // Either way it's a graceful no-op — never overwrite an existing summary with nothing (no undo).
        var summary = output?.Classification.Summary;
        if (string.IsNullOrWhiteSpace(summary))
            return new SummarizeDocumentResponse { Summary = document.Summary };

        document.ApplySummary(summary, currentUser.UserId);

        await unitOfWork.SaveChangesAsync(ct);

        return new SummarizeDocumentResponse { Summary = document.Summary };
    }
}
