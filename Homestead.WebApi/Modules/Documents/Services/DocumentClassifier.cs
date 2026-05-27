using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

public record DocumentClassificationOutput(
    ClassificationResult Classification,
    List<TagInfo> AvailableTags,
    Guid ProviderId);

public interface IDocumentClassifier
{
    Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, Guid? aiProviderId, CancellationToken ct);

    Task<bool> ClassifyAndApplyAsync(Guid documentId, Guid? aiProviderId, CancellationToken ct);

    Task<bool> IsAutoClassifyEnabledAsync(CancellationToken ct);
}

public class DocumentClassifier(
    AppDbContext db,
    IAiProviderResolver providerResolver,
    IAiClassificationService classificationService,
    ISearchVectorUpdater searchVectorUpdater) : IDocumentClassifier
{
    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, Guid? aiProviderId, CancellationToken ct)
    {
        var provider = await providerResolver.ResolveAsync(aiProviderId, ct);
        if (provider is null)
            return null;

        var latestVersion = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var allTags = await db.Tags
            .AsNoTracking()
            .Select(t => new TagInfo(t.Id, t.Name, t.Path))
            .ToListAsync(ct);

        var result = await classificationService.ClassifyAsync(
            provider, documentName, latestVersion?.ExtractedText, allTags, ct);

        if (result is null)
            return null;

        return new DocumentClassificationOutput(result, allTags, provider.Id);
    }

    public async Task<bool> ClassifyAndApplyAsync(Guid documentId, Guid? aiProviderId, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new InvalidOperationException($"Document {documentId} not found.");

        var output = await ClassifyAsync(documentId, document.Name, aiProviderId, ct);
        if (output is null)
            return false;

        if (ClearPendingSuggestions(document))
            await db.SaveChangesAsync(ct);

        ApplyTags(document, output.Classification.MatchedTagIds, output.ProviderId);
        ApplyName(document, output.Classification.SuggestedTitle, output.ProviderId);
        ApplySummary(document, output.Classification.Summary);

        await db.SaveChangesAsync(ct);
        await searchVectorUpdater.UpdateSearchVectorAsync(documentId, ct);

        return true;
    }

    public async Task<bool> IsAutoClassifyEnabledAsync(CancellationToken ct)
    {
        var settingsEntity = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);

        if (settingsEntity is null)
            return false;

        var settings = JsonSerializer.Deserialize<DocumentSettings>(settingsEntity.Value, FieldChange.JsonOptions);
        return settings?.AutoClassifyOnUpload == true;
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

    private static void ApplySummary(Document document, string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        document.Summary = summary;
    }
}
