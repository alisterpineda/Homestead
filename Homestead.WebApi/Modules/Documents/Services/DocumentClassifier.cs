using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

public record DocumentClassificationOutput(
    ClassificationResult Classification,
    List<TagInfo> AvailableTags);

public interface IDocumentClassifier
{
    /// <summary>
    /// Classifies an existing document by loading its latest version's extracted text. Pure — performs
    /// no document writes. Returns null when AI is not configured or the model produced nothing.
    /// </summary>
    Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, ClassificationScope scope, CancellationToken ct);

    /// <summary>
    /// Classifies from an in-memory document name + extracted text — used during upload, before the
    /// document exists. Pure — performs no document writes.
    /// </summary>
    Task<DocumentClassificationOutput?> ClassifyAsync(
        string documentName, string? extractedText, ClassificationScope scope, CancellationToken ct);

    /// <summary>
    /// Applies a classification result to a tracked (or newly-constructed) document as a net diff —
    /// adds only newly-desired tags/parties and removes only AI tags/parties no longer matched, never
    /// remove-then-re-adding the same key (which would collide on the DocumentTag composite key). Sets
    /// the DocumentTag.Tag navigation on added links so an in-memory <c>DocumentSnapshot</c> diff resolves
    /// Tag.Path without a re-read. Records pending suggestions for AI-applied values. Does not save or
    /// audit — the caller's command handler owns the commit (and the Created/Updated audit decision).
    /// </summary>
    Task ApplyAsync(
        Document document, ClassificationResult classification, ClassificationScope scope,
        Guid? actorUserId, bool stampSummaryMetadata, CancellationToken ct);

    Task<bool> IsAutoClassifyEnabledAsync(CancellationToken ct);
}

public class DocumentClassifier(
    AppDbContext db,
    IAiClientFactory clientFactory,
    IAiClassificationService classificationService) : IDocumentClassifier
{
    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, ClassificationScope scope, CancellationToken ct)
    {
        // Gate on configured-ness before touching the database so an unconfigured AI integration
        // short-circuits without loading the latest version.
        if (!clientFactory.IsConfigured)
            return null;

        var latestVersion = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        return await ClassifyCoreAsync(documentName, latestVersion?.ExtractedText, scope, ct);
    }

    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        string documentName, string? extractedText, ClassificationScope scope, CancellationToken ct)
    {
        if (!clientFactory.IsConfigured)
            return null;

        return await ClassifyCoreAsync(documentName, extractedText, scope, ct);
    }

    private async Task<DocumentClassificationOutput?> ClassifyCoreAsync(
        string documentName, string? extractedText, ClassificationScope scope, CancellationToken ct)
    {
        // Only load a catalog when its field is in scope — mirrors the service omitting it from the prompt.
        var allTags = scope.HasFlag(ClassificationScope.Tags)
            ? await db.Tags
                .AsNoTracking()
                .Select(t => new TagInfo(t.Id, t.Name, t.Path))
                .ToListAsync(ct)
            : [];

        var allParties = scope.HasFlag(ClassificationScope.Parties)
            ? await db.Parties
                .AsNoTracking()
                .Select(p => new PartyInfo(p.Id, p.Name, p.Description))
                .ToListAsync(ct)
            : [];

        var result = await classificationService.ClassifyAsync(
            documentName, extractedText, allTags, allParties, scope, ct);

        if (result is null)
            return null;

        return new DocumentClassificationOutput(result, allTags);
    }

    public async Task ApplyAsync(
        Document document, ClassificationResult classification, ClassificationScope scope,
        Guid? actorUserId, bool stampSummaryMetadata, CancellationToken ct)
    {
        if (scope.HasFlag(ClassificationScope.Tags))
            await ApplyTagsAsync(document, classification.MatchedTagIds, ct);
        if (scope.HasFlag(ClassificationScope.Parties))
            await ApplyPartiesAsync(document, classification.MatchedPartyIds, ct);
        if (scope.HasFlag(ClassificationScope.Name))
            ApplyName(document, classification.SuggestedTitle);
        if (scope.HasFlag(ClassificationScope.Summary))
            ApplySummary(document, classification.Summary, actorUserId, stampSummaryMetadata);
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

    private async Task ApplyTagsAsync(Document document, List<Guid> matchedTagIds, CancellationToken ct)
    {
        // Resolve the matched tags that actually exist — gives us tracked entities to set the .Tag nav
        // and silently drops any stale ids the model may have returned.
        var matchedTags = await db.Tags
            .Where(t => matchedTagIds.Contains(t.Id))
            .ToListAsync(ct);
        var matchedById = matchedTags.ToDictionary(t => t.Id);
        var matched = matchedById.Keys.ToHashSet();

        // Clearing the in-scope pending suggestions makes their AI-applied tags candidates for removal.
        var oldAiSuggestions = document.Suggestions
            .Where(s => s.Field == DocumentSuggestionField.Tags && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();
        var oldAiTagIds = oldAiSuggestions
            .Select(s => Guid.TryParse(s.SuggestedValue, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue).Select(g => g!.Value)
            .ToHashSet();
        foreach (var s in oldAiSuggestions)
            db.DocumentSuggestions.Remove(s);

        var existingTagIds = document.DocumentTags.Select(dt => dt.TagId).ToHashSet();
        var userTagIds = existingTagIds.Except(oldAiTagIds).ToHashSet(); // user/approved tags — always kept

        var desiredTagIds = new HashSet<Guid>(userTagIds);
        desiredTagIds.UnionWith(matched);

        // Remove only old AI tags no longer matched; re-matched tags stay put (no composite-key churn).
        foreach (var dt in document.DocumentTags.Where(dt => !desiredTagIds.Contains(dt.TagId)).ToList())
            document.DocumentTags.Remove(dt);

        // Add genuinely new links (desired \ existing reduces to matched-but-not-linked).
        foreach (var tagId in desiredTagIds.Where(id => !existingTagIds.Contains(id)))
        {
            document.DocumentTags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                TagId = tagId,
                Tag = matchedById[tagId]
            });
        }

        // A fresh pending suggestion for each AI-applied tag (matched but not a user tag).
        foreach (var tagId in matched.Where(id => !userTagIds.Contains(id)))
        {
            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                DocumentId = document.Id,
                Field = DocumentSuggestionField.Tags,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = tagId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task ApplyPartiesAsync(Document document, List<Guid> matchedPartyIds, CancellationToken ct)
    {
        var matched = (await db.Parties
                .Where(p => matchedPartyIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var oldAiSuggestions = document.Suggestions
            .Where(s => s.Field == DocumentSuggestionField.Parties && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();
        var oldAiPartyIds = oldAiSuggestions
            .Select(s => Guid.TryParse(s.SuggestedValue, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue).Select(g => g!.Value)
            .ToHashSet();
        foreach (var s in oldAiSuggestions)
            db.DocumentSuggestions.Remove(s);

        var existingPartyIds = document.DocumentParties.Select(dp => dp.PartyId).ToHashSet();
        var userPartyIds = existingPartyIds.Except(oldAiPartyIds).ToHashSet();

        var desiredPartyIds = new HashSet<Guid>(userPartyIds);
        desiredPartyIds.UnionWith(matched);

        foreach (var dp in document.DocumentParties.Where(dp => !desiredPartyIds.Contains(dp.PartyId)).ToList())
            document.DocumentParties.Remove(dp);

        foreach (var partyId in desiredPartyIds.Where(id => !existingPartyIds.Contains(id)))
        {
            document.DocumentParties.Add(new DocumentParty
            {
                DocumentId = document.Id,
                PartyId = partyId
            });
        }

        foreach (var partyId in matched.Where(id => !userPartyIds.Contains(id)))
        {
            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                DocumentId = document.Id,
                Field = DocumentSuggestionField.Parties,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = partyId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private void ApplyName(Document document, string suggestedTitle)
    {
        // Clear any pending name suggestion and recover the true original name (its OriginalValue).
        var oldNameSuggestions = document.Suggestions
            .Where(s => s.Field == DocumentSuggestionField.Name && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();

        var originalName = document.Name;
        foreach (var s in oldNameSuggestions)
        {
            if (s.OriginalValue is not null)
                originalName = s.OriginalValue;
            db.DocumentSuggestions.Remove(s);
        }

        // Revert to the original; re-apply only when the model proposes a genuinely different title.
        document.Name = originalName;

        if (string.IsNullOrWhiteSpace(suggestedTitle) || suggestedTitle == originalName)
            return;

        document.Name = suggestedTitle;

        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = document.Id,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = suggestedTitle,
            OriginalValue = originalName,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void ApplySummary(Document document, string summary, Guid? actorUserId, bool stampMetadata)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        // On the existing-document path a written summary is a real modification — stamp modified-metadata
        // via the shared helper. On the creation path the summary is part of the initial state, so set it
        // directly and leave the document reading as created-only.
        if (stampMetadata)
            document.ApplySummary(summary, actorUserId);
        else
            document.Summary = summary;
    }
}
