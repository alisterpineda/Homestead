using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class DocumentClassifierTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAiProviderResolver _providerResolver;
    private readonly IAiClassificationService _classificationService;
    private readonly DocumentClassifier _sut;

    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Guid _providerId = Guid.NewGuid();

    public DocumentClassifierTests()
    {
        _db = TestAppDbContext.Create();
        _providerResolver = Substitute.For<IAiProviderResolver>();
        _classificationService = Substitute.For<IAiClassificationService>();
        _sut = new DocumentClassifier(_db, _providerResolver, _classificationService);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---- ClassifyAsync (pure — no document writes) -------------------------------------------------

    [Fact]
    public async Task ClassifyAsync_ResolvesProvider_LoadsInputs_CallsAiService()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        var provider = NewProvider();

        SeedDocumentWithVersion("extracted text here");
        SeedTag(tagId, "Finance", "Finance");

        _providerResolver.ResolveAsync(null, ct).Returns(provider);
        _classificationService.ClassifyAsync(
            provider, "Test Doc", "extracted text here",
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([tagId], [], "A summary", "Better Title"));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.All, ct);

        Assert.NotNull(result);
        Assert.Equal("Better Title", result!.Classification.SuggestedTitle);
        Assert.Equal("A summary", result.Classification.Summary);
        Assert.Single(result.Classification.MatchedTagIds);
        Assert.Equal(tagId, result.Classification.MatchedTagIds[0]);
        Assert.Single(result.AvailableTags);
    }

    [Fact]
    public async Task ClassifyAsync_NoProvider_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        _providerResolver.ResolveAsync(null, ct).Returns((AiProvider?)null);

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.All, ct);

        Assert.Null(result);
        await _classificationService.DidNotReceive().ClassifyAsync(
            Arg.Any<AiProvider>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AiServiceReturnsNull_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var provider = NewProvider();
        SeedDocumentWithVersion("some text");

        _providerResolver.ResolveAsync(null, ct).Returns(provider);
        _classificationService.ClassifyAsync(
            provider, "Test Doc", "some text",
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns((ClassificationResult?)null);

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.All, ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClassifyAsync_UsesLatestVersion()
    {
        var ct = TestContext.Current.CancellationToken;

        var provider = NewProvider();

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _documentId,
            VersionNumber = 1,
            ExtractedText = "old text",
            ContentType = "application/pdf",
            FileSize = 100,
            FilePath = "test/old.pdf",
            FileHash = "oldhash",
            FileExtension = ".pdf"
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _documentId,
            VersionNumber = 2,
            ExtractedText = "latest text",
            ContentType = "application/pdf",
            FileSize = 200,
            FilePath = "test/new.pdf",
            FileHash = "newhash",
            FileExtension = ".pdf"
        });
        await _db.SaveChangesAsync(ct);

        _providerResolver.ResolveAsync(null, ct).Returns(provider);
        _classificationService.ClassifyAsync(
            provider, "Test Doc", "latest text",
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "summary", "title"));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.All, ct);

        Assert.NotNull(result);
        await _classificationService.Received(1).ClassifyAsync(
            provider, "Test Doc", "latest text",
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct);
    }

    [Fact]
    public async Task ClassifyAsync_InMemoryOverload_UsesProvidedText_NoDocumentLoad()
    {
        // The in-memory overload classifies pre-creation (no persisted document/version).
        var ct = TestContext.Current.CancellationToken;
        var provider = NewProvider();

        _providerResolver.ResolveAsync(null, ct).Returns(provider);
        _classificationService.ClassifyAsync(
            provider, "Upload Name", "in-memory text",
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "summary", "title"));

        var result = await _sut.ClassifyAsync("Upload Name", "in-memory text", null, ClassificationScope.All, ct);

        Assert.NotNull(result);
        await _classificationService.Received(1).ClassifyAsync(
            provider, "Upload Name", "in-memory text",
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct);
    }

    // ---- ApplyAsync (net-diff onto a tracked/new document; no save, no audit) ----------------------

    [Fact]
    public async Task ApplyAsync_AppliesClassification_TagsNameSummary()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "AI summary", "AI Title"),
            _providerId, ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("AI Title", doc!.Name);
        Assert.Equal("AI summary", doc.Summary);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Name);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Tags);
    }

    [Fact]
    public async Task ApplyAsync_SetsTagNavigation_SoSnapshotResolvesTagPath()
    {
        // The applied DocumentTag carries its Tag navigation, so an in-memory DocumentSnapshot diff
        // resolves Tag.Path without a re-read (this is what feeds the Updated audit at the chokepoint).
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Root/Finance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "", "Test Doc"),
            _providerId, ClassificationScope.Tags, actorUserId: null, stampSummaryMetadata: false, ct);

        var added = document.DocumentTags.Single(dt => dt.TagId == tagId);
        Assert.NotNull(added.Tag);
        Assert.Equal("Root/Finance", added.Tag.Path);
    }

    [Fact]
    public async Task ApplyAsync_AppliesParties_AndCreatesPendingSuggestions()
    {
        var ct = TestContext.Current.CancellationToken;
        var partyId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedParty(partyId, "ACME Insurance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [partyId], "AI summary", "Test Doc"),
            _providerId, ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var links = _db.DocumentParties.Where(dp => dp.DocumentId == _documentId).ToList();
        Assert.Single(links);
        Assert.Equal(partyId, links[0].PartyId);

        var suggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Parties)
            .ToList();
        Assert.Single(suggestions);
        Assert.Equal(partyId.ToString(), suggestions[0].SuggestedValue);
        Assert.Equal(DocumentSuggestionStatus.Pending, suggestions[0].Status);
    }

    [Fact]
    public async Task ApplyAsync_SkipsAlreadyLinkedUserParty_NoSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;
        var partyId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedParty(partyId, "ACME Insurance");
        _db.DocumentParties.Add(new DocumentParty { DocumentId = _documentId, PartyId = partyId });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [partyId], "AI summary", "Test Doc"),
            _providerId, ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var links = _db.DocumentParties.Where(dp => dp.DocumentId == _documentId).ToList();
        Assert.Single(links);
        var partySuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Parties)
            .ToList();
        Assert.Empty(partySuggestions);
    }

    [Fact]
    public async Task ApplyAsync_ClearsPendingPartySuggestions_BeforeApplyingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldPartyId = Guid.NewGuid();
        var newPartyId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedParty(oldPartyId, "Old Party");
        SeedParty(newPartyId, "New Party");

        _db.DocumentParties.Add(new DocumentParty { DocumentId = _documentId, PartyId = oldPartyId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Parties,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = oldPartyId.ToString(),
            AiProviderId = _providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [newPartyId], "new summary", "Test Doc"),
            _providerId, ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        // Old AI-applied party link is removed; new one is linked.
        var linkedPartyIds = _db.DocumentParties
            .Where(dp => dp.DocumentId == _documentId)
            .Select(dp => dp.PartyId)
            .ToList();
        Assert.DoesNotContain(oldPartyId, linkedPartyIds);
        Assert.Contains(newPartyId, linkedPartyIds);

        var pendingSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Status == DocumentSuggestionStatus.Pending)
            .Select(s => s.SuggestedValue)
            .ToList();
        Assert.DoesNotContain(oldPartyId.ToString(), pendingSuggestions);
        Assert.Contains(newPartyId.ToString(), pendingSuggestions);
    }

    [Fact]
    public async Task ApplyAsync_ClearsPendingSuggestions_BeforeApplyingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(oldTagId, "OldTag", "OldTag");
        SeedTag(newTagId, "NewTag", "NewTag");

        _db.DocumentTags.Add(new DocumentTag { DocumentId = _documentId, TagId = oldTagId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Tags,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = oldTagId.ToString(),
            AiProviderId = _providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "Old AI Name",
            OriginalValue = "Test Doc",
            AiProviderId = _providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([newTagId], [], "new summary", "New Title"),
            _providerId, ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var pendingSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();

        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == oldTagId.ToString());
        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == "Old AI Name");
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == newTagId.ToString());
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == "New Title");
    }

    [Fact]
    public async Task ApplyAsync_RematchedTag_KeepsLink_NoCompositeKeyChurn()
    {
        // Canary for the net-diff: re-matching an already-AI-applied tag must NOT remove-then-re-add the
        // DocumentTag (which would collide on the composite key). The link stays; the suggestion refreshes.
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        _db.DocumentTags.Add(new DocumentTag { DocumentId = _documentId, TagId = tagId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Tags,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = tagId.ToString(),
            AiProviderId = _providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "", "Test Doc"),
            _providerId, ClassificationScope.Tags, actorUserId: null, stampSummaryMetadata: false, ct);
        // No InvalidOperationException for a duplicate-tracked DocumentTag.
        await _db.SaveChangesAsync(ct);

        var links = _db.DocumentTags.Where(dt => dt.DocumentId == _documentId).ToList();
        Assert.Single(links);
        Assert.Equal(tagId, links[0].TagId);

        var pendingTagSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Tags
                && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();
        Assert.Single(pendingTagSuggestions);
    }

    [Fact]
    public async Task ApplyAsync_ScopedToTags_LeavesPendingNameSuggestionUntouched()
    {
        // Headline product rule: refreshing one field must not clear another field's pending suggestions.
        var ct = TestContext.Current.CancellationToken;
        var oldTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(oldTagId, "OldTag", "OldTag");
        SeedTag(newTagId, "NewTag", "NewTag");

        _db.DocumentTags.Add(new DocumentTag { DocumentId = _documentId, TagId = oldTagId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Tags,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = oldTagId.ToString(),
            AiProviderId = _providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "Old AI Name",
            OriginalValue = "Test Doc",
            AiProviderId = _providerId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([newTagId], [], "new summary", "New Title"),
            _providerId, ClassificationScope.Tags, actorUserId: null, stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        // Tags: old suggestion cleared, new one applied.
        var tagSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Tags
                && s.Status == DocumentSuggestionStatus.Pending)
            .Select(s => s.SuggestedValue)
            .ToList();
        Assert.DoesNotContain(oldTagId.ToString(), tagSuggestions);
        Assert.Contains(newTagId.ToString(), tagSuggestions);

        // Name: out of scope, so its pending suggestion survives untouched.
        var nameSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Name)
            .ToList();
        Assert.Single(nameSuggestions);
        Assert.Equal(DocumentSuggestionStatus.Pending, nameSuggestions[0].Status);
        Assert.Equal("Old AI Name", nameSuggestions[0].SuggestedValue);
    }

    [Fact]
    public async Task ApplyAsync_ScopedToSummary_StampsMetadata_NoSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithVersion("some text");
        var document = LoadDocument();
        var userId = Guid.NewGuid();

        await _sut.ApplyAsync(document, new ClassificationResult([], [], "The generated summary", "Test Doc"),
            _providerId, ClassificationScope.Summary, actorUserId: userId, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("The generated summary", doc!.Summary);
        // Writing a summary on the existing-doc path stamps modified-metadata.
        Assert.Equal(userId, doc.UpdatedByUserId);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task ApplyAsync_SummaryWithoutStamp_WritesSummary_LeavesMetadataClean()
    {
        // Creation path: the summary is part of the initial state, so no modified-metadata stamp —
        // the document reads as created-only.
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithVersion("some text");
        var document = LoadDocument();
        var userId = Guid.NewGuid();

        await _sut.ApplyAsync(document, new ClassificationResult([], [], "The generated summary", "Test Doc"),
            _providerId, ClassificationScope.Summary, actorUserId: userId, stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("The generated summary", doc!.Summary);
        Assert.Null(doc.UpdatedByUserId);
    }

    [Fact]
    public async Task ApplyAsync_ScopeWithoutName_LeavesDocumentNameUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "summary", "Should Not Apply"),
            _providerId, ClassificationScope.Tags | ClassificationScope.Parties, actorUserId: null,
            stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("Test Doc", doc!.Name);

        var nameSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Name)
            .ToList();
        Assert.Empty(nameSuggestions);
    }

    // ---- IsAutoClassifyEnabledAsync ---------------------------------------------------------------

    [Fact]
    public async Task IsAutoClassifyEnabledAsync_ReturnsTrue_WhenEnabled()
    {
        var ct = TestContext.Current.CancellationToken;

        var settings = new DocumentSettings { AutoClassifyOnUpload = true };
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = JsonSerializer.Serialize(settings, FieldChange.JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.IsAutoClassifyEnabledAsync(ct);

        Assert.True(result);
    }

    [Fact]
    public async Task IsAutoClassifyEnabledAsync_ReturnsFalse_WhenDisabled()
    {
        var ct = TestContext.Current.CancellationToken;

        var settings = new DocumentSettings { AutoClassifyOnUpload = false };
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = JsonSerializer.Serialize(settings, FieldChange.JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.IsAutoClassifyEnabledAsync(ct);

        Assert.False(result);
    }

    [Fact]
    public async Task IsAutoClassifyEnabledAsync_ReturnsFalse_WhenNoSettings()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.IsAutoClassifyEnabledAsync(ct);

        Assert.False(result);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private AiProvider NewProvider() => new()
    {
        Id = _providerId,
        Name = "Test Provider",
        ProviderType = AiProviderType.AzureAIFoundry,
        Configuration = "{}",
        IsEnabled = true
    };

    private Document LoadDocument() => _db.Documents
        .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
        .Include(d => d.DocumentParties)
        .Include(d => d.Suggestions)
        .First(d => d.Id == _documentId);

    private void SeedDocumentWithVersion(string? extractedText)
    {
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _documentId,
            VersionNumber = 1,
            ExtractedText = extractedText,
            ContentType = "application/pdf",
            FileSize = 100,
            FilePath = "test/file.pdf",
            FileHash = "abc123",
            FileExtension = ".pdf"
        });
        _db.SaveChanges();
    }

    private void SeedTag(Guid id, string name, string path)
    {
        _db.Tags.Add(new Tag
        {
            Id = id,
            Name = name,
            Path = path,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }

    private void SeedParty(Guid id, string name)
    {
        _db.Parties.Add(new Party
        {
            Id = id,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }
}
