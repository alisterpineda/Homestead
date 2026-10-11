using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;

public class ApproveSuggestionHandler(AppDbContext db, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid documentId, Guid suggestionId, CancellationToken ct)
    {
        var suggestion = await db.DocumentSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId && s.DocumentId == documentId, ct)
            ?? throw new EntityNotFoundException("Suggestion not found.");

        if (suggestion.Status != DocumentSuggestionStatus.Pending)
            throw new BusinessRuleException("Suggestion has already been resolved.");

        suggestion.Status = DocumentSuggestionStatus.Approved;
        suggestion.ResolvedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
    }
}
