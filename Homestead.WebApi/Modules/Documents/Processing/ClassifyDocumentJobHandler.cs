using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ClassifyDocumentJobHandler(
    AppDbContext db,
    IDocumentClassifier classifier,
    ISearchVectorUpdater searchVectorUpdater,
    IProcessingNotifier notifier) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ClassifyDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ClassifyDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var document = await db.Documents
            .Include(d => d.DocumentTags)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == payload.DocumentId, ct)
            ?? throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

        job.EntityId = payload.DocumentId;

        var output = await classifier.ClassifyAsync(
            payload.DocumentId, document.Name, payload.AiProviderId, ct);

        if (output is not null)
        {
            // Persist deletes first so the subsequent inserts don't collide
            // with Deleted entities sharing the same composite key (DocumentTag).
            if (ClearPendingSuggestions(document))
                await db.SaveChangesAsync(ct);

            ApplyTags(document, output.Classification.MatchedTagIds, output.ProviderId);
            ApplyName(document, output.Classification.SuggestedTitle, output.ProviderId);
            ApplySummary(document, output.Classification.Summary, output.ProviderId);

            await db.SaveChangesAsync(ct);
            await searchVectorUpdater.UpdateSearchVectorAsync(payload.DocumentId, ct);
        }

        if (job.ParentJobId is null)
        {
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                payload.DocumentId,
                document.Name,
                error: null,
                job.CreatedByUserId,
                ct);
        }
    }

    private bool ClearPendingSuggestions(Document document)
    {
        var pendingSuggestions = document.Suggestions
            .Where(s => s.Status == DocumentSuggestionStatus.Pending)
            .ToList();

        if (pendingSuggestions.Count == 0)
            return false;

        foreach (var suggestion in pendingSuggestions)
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
                        document.Name = suggestion.OriginalValue;
                    break;

                case DocumentSuggestionField.Summary:
                    document.Summary = null;
                    break;
            }

            db.DocumentSuggestions.Remove(suggestion);
        }

        return true;
    }

    private void ApplyTags(Document document, List<Guid> tagIds, Guid providerId)
    {
        var existingTagIds = new HashSet<Guid>(document.DocumentTags.Select(dt => dt.TagId));

        foreach (var tagId in tagIds)
        {
            if (existingTagIds.Contains(tagId))
                continue;

            document.DocumentTags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                TagId = tagId
            });

            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                DocumentId = document.Id,
                Field = DocumentSuggestionField.Tags,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = tagId.ToString(),
                AiProviderId = providerId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private void ApplyName(Document document, string suggestedTitle, Guid providerId)
    {
        if (string.IsNullOrWhiteSpace(suggestedTitle) || suggestedTitle == document.Name)
            return;

        var originalName = document.Name;
        document.Name = suggestedTitle;

        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = document.Id,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = suggestedTitle,
            OriginalValue = originalName,
            AiProviderId = providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private void ApplySummary(Document document, string summary, Guid providerId)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        document.Summary = summary;

        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = document.Id,
            Field = DocumentSuggestionField.Summary,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = summary,
            AiProviderId = providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
