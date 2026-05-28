using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;

public class DeclineSuggestionHandler(AppDbContext db, ISearchVectorUpdater searchVectorUpdater)
    : IRequestHandler<DeclineSuggestionCommand>
{
    public async Task Handle(DeclineSuggestionCommand request, CancellationToken ct)
    {
        var suggestion = await db.DocumentSuggestions
            .Include(s => s.Document)
            .ThenInclude(d => d.DocumentTags)
            .Include(s => s.Document)
            .ThenInclude(d => d.DocumentParties)
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId && s.DocumentId == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Suggestion not found.");

        if (suggestion.Status != DocumentSuggestionStatus.Pending)
            throw new BusinessRuleException("Suggestion has already been resolved.");

        var needsSearchUpdate = DocumentSuggestionReverter.Revert(suggestion.Document, suggestion);

        suggestion.Status = DocumentSuggestionStatus.Declined;
        suggestion.ResolvedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        if (needsSearchUpdate)
            await searchVectorUpdater.UpdateSearchVectorAsync(request.DocumentId, ct);
    }
}
