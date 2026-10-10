using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;

public class DeclineAllSuggestionsHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task Handle(DeclineAllSuggestionsCommand request, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct);

        if (document is null)
            return;

        // Declining reverts the document — audited as a single net change. Capture the before-state
        // before any revert; the diff is computed against the post-revert entity at flush time.
        audit.Track<DocumentSnapshot, Document>(document, "Document", document.Id);

        var now = DateTimeOffset.UtcNow;

        foreach (var suggestion in document.Suggestions.ToList())
        {
            DocumentSuggestionReverter.Revert(document, suggestion);

            suggestion.Status = DocumentSuggestionStatus.Declined;
            suggestion.ResolvedAt = now;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
