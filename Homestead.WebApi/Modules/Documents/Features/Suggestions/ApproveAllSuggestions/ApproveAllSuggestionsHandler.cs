using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;

public class ApproveAllSuggestionsHandler(AppDbContext db, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid documentId, CancellationToken ct)
    {
        var pendingSuggestions = await db.DocumentSuggestions
            .Where(s => s.DocumentId == documentId && s.Status == DocumentSuggestionStatus.Pending)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var suggestion in pendingSuggestions)
        {
            suggestion.Status = DocumentSuggestionStatus.Approved;
            suggestion.ResolvedAt = now;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
