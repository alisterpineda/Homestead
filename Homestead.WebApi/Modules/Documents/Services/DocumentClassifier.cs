using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Services;

public record DocumentClassificationOutput(
    ClassificationResult Classification,
    List<TagInfo> AvailableTags);

// The stored file behind a classify call, from which DocumentClassifier picks the input (the PDF itself, a page-1
// render, or neither — ClassificationInputSelector).
public record ClassificationFile(string FileHash, string PhysicalPath, string ContentType, long FileSize);

public interface IDocumentClassifier
{
    /// <summary>
    /// Classifies an existing document by loading its latest version's extracted text. Pure — performs
    /// no document writes. Returns null when AI is not configured or the model produced nothing.
    /// When <paramref name="attachImage"/> is true and the scope/config allow it, sends the PDF itself or the
    /// page-1 image (see <see cref="ClassificationInputSelector"/>); synchronous callers (the synchronous
    /// summarize endpoint) pass <c>false</c> so the slow document prefill only ever runs on the background-job path.
    /// </summary>
    Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, ClassificationScope scope, CancellationToken ct,
        bool attachImage = true);

    /// <summary>
    /// Classifies from an in-memory document name + extracted text and the stored file (from which the PDF or a
    /// page render is taken, as for the overload above) — used during upload, before the document exists. Pure —
    /// performs no document writes.
    /// </summary>
    Task<DocumentClassificationOutput?> ClassifyAsync(
        string documentName, string? extractedText, ClassificationFile? file, ClassificationScope scope,
        CancellationToken ct);

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
    IAiProvider aiProvider,
    AiInputSettings aiSettings,
    IAiClassificationService classificationService,
    AiPageRenderService aiPageRenderService,
    IOptions<FileStorageOptions> storageOptions,
    ILogger<DocumentClassifier> logger) : IDocumentClassifier
{
    private readonly FileStorageOptions _storageOptions = storageOptions.Value;

    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        Guid documentId, string documentName, ClassificationScope scope, CancellationToken ct,
        bool attachImage = true)
    {
        // Gate on AI being available before touching the database so an unconfigured (or broken) AI
        // integration short-circuits without loading the latest version.
        if (!aiProvider.Availability.IsConfigured)
            return null;

        var latestVersion = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        // The file is offered only when the caller allows it (synchronous endpoints opt out — document-carrying
        // classification must stay on the background path); whether the PDF or a render is then sent is the
        // selector's call.
        var file = attachImage && latestVersion is not null
            ? new ClassificationFile(
                latestVersion.FileHash,
                latestVersion.GetPhysicalPath(_storageOptions.MediaPath),
                latestVersion.ContentType,
                latestVersion.FileSize)
            : null;

        return await ClassifyCoreAsync(documentName, latestVersion?.ExtractedText, file, scope, ct);
    }

    public async Task<DocumentClassificationOutput?> ClassifyAsync(
        string documentName, string? extractedText, ClassificationFile? file, ClassificationScope scope,
        CancellationToken ct)
    {
        if (!aiProvider.Availability.IsConfigured)
            return null;

        return await ClassifyCoreAsync(documentName, extractedText, file, scope, ct);
    }

    private async Task<DocumentClassificationOutput?> ClassifyCoreAsync(
        string documentName, string? extractedText, ClassificationFile? file, ClassificationScope scope,
        CancellationToken ct)
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

        var mode = file is null ? ClassificationInputMode.Text : SelectInputMode(file, scope);

        // In PDF mode the bytes are read from disk and no render is made (so the render cache isn't warmed for a
        // later statement import — harmless, it renders then). Otherwise the page-1 render is cached by FileHash +
        // edge, so a re-classify reuses an upload-warmed one.
        var pdf = mode == ClassificationInputMode.Pdf ? await File.ReadAllBytesAsync(file!.PhysicalPath, ct) : null;
        var pageImage = mode == ClassificationInputMode.TextAndImage ? await RenderAsync(file!, ct) : null;

        ClassificationResult? result;
        try
        {
            // PDF mode sends no OCR text: the PDF replaces it.
            result = await classificationService.ClassifyAsync(
                new ClassificationInput(documentName, pdf is null ? extractedText : null, pageImage, pdf),
                allTags, allParties, scope, ct);
        }
        catch (Exception ex) when (pdf is not null && IsPdfRejection(ex, out var status))
        {
            // The API refused this PDF (a 4xx that a retry can't fix — e.g. a document it can't parse). Retry once
            // on the text + render path, which copes with anything OCR managed, rather than failing the classify.
            logger.LogWarning(ex,
                "PDF input for document '{Name}' was rejected ({Status}); retrying once on extracted text and the page render",
                documentName, status);
            var fallback = ClassificationInputSelector.SelectWithoutPdf(aiSettings, scope);
            pageImage = fallback == ClassificationInputMode.TextAndImage ? await RenderAsync(file!, ct) : null;
            result = await classificationService.ClassifyAsync(
                new ClassificationInput(documentName, extractedText, pageImage, null), allTags, allParties, scope, ct);
        }

        if (result is null)
            return null;

        return new DocumentClassificationOutput(result, allTags);
    }

    // The cheap gates first, so only a PDF that could qualify is opened for its page count and encryption flag.
    private ClassificationInputMode SelectInputMode(ClassificationFile file, ClassificationScope scope)
    {
        if (!ClassificationInputSelector.IsPdfCandidate(aiSettings, scope, file.ContentType, file.FileSize))
            return ClassificationInputSelector.Select(aiSettings, scope, file.ContentType, file.FileSize, null, false);

        var facts = ClassificationInputSelector.ReadPdfFacts(file.PhysicalPath);
        return ClassificationInputSelector.Select(
            aiSettings, scope, file.ContentType, file.FileSize, facts?.PageCount, facts?.IsEncrypted ?? false);
    }

    private Task<byte[]?> RenderAsync(ClassificationFile file, CancellationToken ct) =>
        aiPageRenderService.GetOrCreateAsync(
            file.FileHash, file.PhysicalPath, file.ContentType, aiSettings.ImageRenderMaxEdge, ct);

    // A 4xx the API won't change its mind about. Excluded: auth and not-found (401/403/404 — a bad key, model ID or
    // endpoint fails the text path the same way), and the transient 408/429, which the job's retry policy handles.
    internal static bool IsPdfRejection(Exception exception, out int status) =>
        AiProviderErrors.TryGetStatus(exception, out status, out _)
        && status is >= 400 and < 500 and not (401 or 403 or 404 or 408 or 429);

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
