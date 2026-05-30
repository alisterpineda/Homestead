using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;

public class ApproveSuggestionHandler(AppDbContext db, IUnitOfWork unitOfWork)
    : IRequestHandler<ApproveSuggestionCommand>
{
    public async Task Handle(ApproveSuggestionCommand request, CancellationToken ct)
    {
        var suggestion = await db.DocumentSuggestions
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId && s.DocumentId == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Suggestion not found.");

        if (suggestion.Status != DocumentSuggestionStatus.Pending)
            throw new BusinessRuleException("Suggestion has already been resolved.");

        suggestion.Status = DocumentSuggestionStatus.Approved;
        suggestion.ResolvedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
    }
}
