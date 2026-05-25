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
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId && s.DocumentId == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Suggestion not found.");

        if (suggestion.Status != DocumentSuggestionStatus.Pending)
            throw new BusinessRuleException("Suggestion has already been resolved.");

        var needsSearchUpdate = false;

        switch (suggestion.Field)
        {
            case DocumentSuggestionField.Tags:
                var tagId = Guid.Parse(suggestion.SuggestedValue);
                var docTag = suggestion.Document.DocumentTags.FirstOrDefault(dt => dt.TagId == tagId);
                if (docTag is not null)
                    suggestion.Document.DocumentTags.Remove(docTag);
                break;

            case DocumentSuggestionField.Name:
                if (suggestion.OriginalValue is not null)
                {
                    suggestion.Document.Name = suggestion.OriginalValue;
                    needsSearchUpdate = true;
                }
                break;

            case DocumentSuggestionField.Summary:
                suggestion.Document.Summary = null;
                needsSearchUpdate = true;
                break;
        }

        suggestion.Status = DocumentSuggestionStatus.Declined;
        suggestion.ResolvedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        if (needsSearchUpdate)
            await searchVectorUpdater.UpdateSearchVectorAsync(request.DocumentId, ct);
    }
}
