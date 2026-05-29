using System.Text.Json;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
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
        Guid documentId, string documentName, Guid? aiProviderId, ClassificationScope scope, CancellationToken ct);

    Task<bool> ClassifyAndApplyAsync(
        Guid documentId, Guid? aiProviderId, ClassificationScope scope, Guid? updatedByUserId,
        bool partOfCreation, CancellationToken ct);

    Task<bool> IsAutoClassifyEnabledAsync(CancellationToken ct);
}

public class DocumentClassifier(
    AppDbContext db,
    IAiProviderResolver providerResolver,
    IAiClassificationService classificationService,
    ISearchVectorUpdater searchVectorUpdater,
    IAuditLogger auditLogger) : IDocumentClassifier
{
    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, Guid? aiProviderId, ClassificationScope scope, CancellationToken ct)
    {
        var provider = await providerResolver.ResolveAsync(aiProviderId, ct);
        if (provider is null)
            return null;

        var latestVersion = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

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
            provider, documentName, latestVersion?.ExtractedText, allTags, allParties, scope, ct);

        if (result is null)
            return null;

        return new DocumentClassificationOutput(result, allTags, provider.Id);
    }

    public async Task<bool> ClassifyAndApplyAsync(
        Guid documentId, Guid? aiProviderId, ClassificationScope scope, Guid? updatedByUserId,
        bool partOfCreation, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .Include(d => d.Suggestions.Where(s => s.Status == DocumentSuggestionStatus.Pending))
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new InvalidOperationException($"Document {documentId} not found.");

        var output = await ClassifyAsync(documentId, document.Name, aiProviderId, scope, ct);
        if (output is null)
            return false;

        // Auto-classify-on-upload is part of document creation (the Created audit entry already
        // covers it), so no Updated row is written. Bulk classify/summarize on existing documents
        // audit the real DB diff (entry vs commit) — capture the "before" snapshot here, before
        // ClearPendingSuggestions mutates the entity.
        var before = partOfCreation ? null : DocumentSnapshot.From(document);

        // Only clear pending suggestions for in-scope fields — refreshing one field must never
        // discard another field's pending suggestions. The intermediate save below avoids
        // DocumentTag composite-key collisions when a just-reverted tag is immediately re-added.
        if (ClearPendingSuggestions(document, scope))
            await db.SaveChangesAsync(ct);

        if (scope.HasFlag(ClassificationScope.Tags))
            ApplyTags(document, output.Classification.MatchedTagIds, output.ProviderId);
        if (scope.HasFlag(ClassificationScope.Parties))
            ApplyParties(document, output.Classification.MatchedPartyIds, output.ProviderId);
        if (scope.HasFlag(ClassificationScope.Name))
            ApplyName(document, output.Classification.SuggestedTitle, output.ProviderId);
        if (scope.HasFlag(ClassificationScope.Summary))
            ApplySummary(document, output.Classification.Summary, updatedByUserId, stampMetadata: !partOfCreation);

        await db.SaveChangesAsync(ct);
        await searchVectorUpdater.UpdateSearchVectorAsync(documentId, ct);

        if (before is not null)
        {
            // Diff entry-state vs committed-state. The "after" snapshot is read fresh (AsNoTracking)
            // so newly-applied tags resolve their Tag.Path from the DB — the tracked entity's new
            // DocumentTags carry only TagId. This net diff also collapses any revert-then-reapply
            // churn from ClearPendingSuggestions.
            //
            // FirstOrDefault (not First): the apply already committed above, so if the document is
            // deleted between that save and this reload (e.g. a concurrent soft-delete), just skip
            // the audit row — failing here would report an already-successful classify as a failed job.
            var after = await db.Documents
                .AsNoTracking()
                .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
                .Include(d => d.DocumentParties)
                .FirstOrDefaultAsync(d => d.Id == documentId, ct);

            if (after is not null)
            {
                var changes = before.Diff(DocumentSnapshot.From(after));
                if (changes.Count > 0)
                {
                    auditLogger.ForUser(updatedByUserId).LogUpdated("Document", documentId, changes);
                    await db.SaveChangesAsync(ct);
                }
            }
        }

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

    private bool ClearPendingSuggestions(Document document, ClassificationScope scope)
    {
        var pendingSuggestions = document.Suggestions
            .Where(s => s.Status == DocumentSuggestionStatus.Pending)
            .Where(s => SuggestionFieldInScope(s.Field, scope))
            .ToList();

        if (pendingSuggestions.Count == 0)
            return false;

        foreach (var suggestion in pendingSuggestions)
        {
            DocumentSuggestionReverter.Revert(document, suggestion);
            db.DocumentSuggestions.Remove(suggestion);
        }

        return true;
    }

    private static bool SuggestionFieldInScope(DocumentSuggestionField field, ClassificationScope scope) =>
        field switch
        {
            DocumentSuggestionField.Tags => scope.HasFlag(ClassificationScope.Tags),
            DocumentSuggestionField.Parties => scope.HasFlag(ClassificationScope.Parties),
            DocumentSuggestionField.Name => scope.HasFlag(ClassificationScope.Name),
            _ => false
        };

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

    private void ApplyParties(Document document, List<Guid> partyIds, Guid providerId)
    {
        var existingPartyIds = new HashSet<Guid>(document.DocumentParties.Select(dp => dp.PartyId));

        foreach (var partyId in partyIds)
        {
            if (existingPartyIds.Contains(partyId))
                continue;

            document.DocumentParties.Add(new DocumentParty
            {
                DocumentId = document.Id,
                PartyId = partyId
            });

            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                DocumentId = document.Id,
                Field = DocumentSuggestionField.Parties,
                Status = DocumentSuggestionStatus.Pending,
                SuggestedValue = partyId.ToString(),
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

    private static void ApplySummary(Document document, string summary, Guid? updatedByUserId, bool stampMetadata)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        document.Summary = summary;

        // On the bulk/refresh path a written summary is a real modification — stamp modified-metadata
        // to match the single-doc summarize path (SummarizeDocumentHandler). On the auto-classify-on-upload
        // path (partOfCreation) the document reads as created-only, so the stamp is skipped.
        if (stampMetadata)
        {
            document.UpdatedAt = DateTimeOffset.UtcNow;
            document.UpdatedByUserId = updatedByUserId;
        }
    }
}
