using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;

public class ApproveAllSuggestionsHandler(AppDbContext db, IUnitOfWork unitOfWork)
    : IRequestHandler<ApproveAllSuggestionsCommand>
{
    public async Task Handle(ApproveAllSuggestionsCommand request, CancellationToken ct)
    {
        var pendingSuggestions = await db.DocumentSuggestions
            .Where(s => s.DocumentId == request.DocumentId && s.Status == DocumentSuggestionStatus.Pending)
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
