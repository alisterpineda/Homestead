using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;

public class DeclineAllSuggestionsHandler(AppDbContext db, ISearchVectorUpdater searchVectorUpdater)
    : IRequestHandler<DeclineAllSuggestionsCommand>
{
    public async Task Handle(DeclineAllSuggestionsCommand request, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags)
            .Include(d => d.DocumentParties)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct);

        if (document is null)
            return;

        var needsSearchUpdate = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var suggestion in document.Suggestions.ToList())
        {
            if (DocumentSuggestionReverter.Revert(document, suggestion))
                needsSearchUpdate = true;

            suggestion.Status = DocumentSuggestionStatus.Declined;
            suggestion.ResolvedAt = now;
        }

        await db.SaveChangesAsync(ct);

        if (needsSearchUpdate)
            await searchVectorUpdater.UpdateSearchVectorAsync(request.DocumentId, ct);
    }
}
