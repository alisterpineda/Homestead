using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public class ClassifyPreviewHandler(AppDbContext db, IDocumentClassifier classifier)
    : IRequestHandler<ClassifyPreviewQuery, ClassifyPreviewResponse>
{
    public async Task<ClassifyPreviewResponse> Handle(ClassifyPreviewQuery request, CancellationToken ct)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(d => d.DocumentTags)
            .Include(d => d.DocumentParties)
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Document not found.");

        var scope = ClassificationScopeExtensions.FromFields(request.Fields);

        var output = await classifier.ClassifyAsync(
            request.DocumentId, document.Name, request.AiProviderId, scope, ct);

        if (output is null)
        {
            return new ClassifyPreviewResponse
            {
                SuggestedTitle = null,
                Summary = null,
                Tags = [],
                Parties = []
            };
        }

        var suggestedTitle = output.Classification.SuggestedTitle;
        if (suggestedTitle == document.Name)
            suggestedTitle = null;

        var existingTagIds = new HashSet<Guid>(document.DocumentTags.Select(dt => dt.TagId));
        var newTagIds = output.Classification.MatchedTagIds
            .Where(id => !existingTagIds.Contains(id))
            .ToHashSet();

        var tags = newTagIds.Count > 0
            ? await db.Tags
                .AsNoTracking()
                .Where(t => newTagIds.Contains(t.Id))
                .Select(t => new ClassifyPreviewTagDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Path = t.Path,
                    Color = t.Color,
                    EffectiveColor = t.EffectiveColor
                })
                .ToListAsync(ct)
            : [];

        var existingPartyIds = new HashSet<Guid>(document.DocumentParties.Select(dp => dp.PartyId));
        var newPartyIds = output.Classification.MatchedPartyIds
            .Where(id => !existingPartyIds.Contains(id))
            .ToHashSet();

        var parties = newPartyIds.Count > 0
            ? await db.Parties
                .AsNoTracking()
                .Where(p => newPartyIds.Contains(p.Id))
                .Select(p => new ClassifyPreviewPartyDto
                {
                    Id = p.Id,
                    Name = p.Name
                })
                .ToListAsync(ct)
            : [];

        return new ClassifyPreviewResponse
        {
            SuggestedTitle = suggestedTitle,
            Summary = null, // always null on classify — Summary is never in classify scope (use summarize)
            Tags = tags,
            Parties = parties
        };
    }
}
