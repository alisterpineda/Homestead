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
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct);

        if (document is null)
            return;

        var needsSearchUpdate = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var suggestion in document.Suggestions.ToList())
        {
            switch (suggestion.Field)
            {
                case DocumentSuggestionField.Tags:
                    var tagId = Guid.Parse(suggestion.SuggestedValue);
                    var docTag = document.DocumentTags.FirstOrDefault(dt => dt.TagId == tagId);
                    if (docTag is not null)
                        document.DocumentTags.Remove(docTag);
                    break;

                case DocumentSuggestionField.Name:
                    if (suggestion.OriginalValue is not null)
                    {
                        document.Name = suggestion.OriginalValue;
                        needsSearchUpdate = true;
                    }
                    break;

                case DocumentSuggestionField.Summary:
                    document.Summary = null;
                    needsSearchUpdate = true;
                    break;
            }

            suggestion.Status = DocumentSuggestionStatus.Declined;
            suggestion.ResolvedAt = now;
        }

        await db.SaveChangesAsync(ct);

        if (needsSearchUpdate)
            await searchVectorUpdater.UpdateSearchVectorAsync(request.DocumentId, ct);
    }
}
