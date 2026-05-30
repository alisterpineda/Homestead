using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;

public class DeclineSuggestionHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeclineSuggestionCommand>
{
    public async Task Handle(DeclineSuggestionCommand request, CancellationToken ct)
    {
        var suggestion = await db.DocumentSuggestions
            .Include(s => s.Document)
            .ThenInclude(d => d.DocumentTags)
            .ThenInclude(dt => dt.Tag)
            .Include(s => s.Document)
            .ThenInclude(d => d.DocumentParties)
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId && s.DocumentId == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Suggestion not found.");

        if (suggestion.Status != DocumentSuggestionStatus.Pending)
            throw new BusinessRuleException("Suggestion has already been resolved.");

        // Declining reverts the document (removes an AI-applied tag/party or restores the original
        // name) — a real document mutation, so it is audited. Capture the before-state ahead of the
        // revert; the diff is computed against the post-revert entity at flush time.
        audit.Track<DocumentSnapshot, Document>(suggestion.Document, "Document", suggestion.DocumentId);

        DocumentSuggestionReverter.Revert(suggestion.Document, suggestion);

        suggestion.Status = DocumentSuggestionStatus.Declined;
        suggestion.ResolvedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
    }
}
