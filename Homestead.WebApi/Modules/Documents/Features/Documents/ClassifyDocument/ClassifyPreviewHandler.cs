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
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new EntityNotFoundException("Document not found.");

        var output = await classifier.ClassifyAsync(
            request.DocumentId, document.Name, request.AiProviderId, ct);

        if (output is null)
        {
            return new ClassifyPreviewResponse
            {
                SuggestedTitle = null,
                Summary = null,
                Tags = []
            };
        }

        var suggestedTitle = output.Classification.SuggestedTitle;
        if (suggestedTitle == document.Name)
            suggestedTitle = null;

        var summary = output.Classification.Summary;
        if (summary == document.Summary)
            summary = null;

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

        return new ClassifyPreviewResponse
        {
            SuggestedTitle = suggestedTitle,
            Summary = summary,
            Tags = tags
        };
    }
}
