using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Modules.Documents.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class DocumentClassifierTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAiProviderResolver _providerResolver;
    private readonly IAiClassificationService _classificationService;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly DocumentClassifier _sut;

    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Guid _providerId = Guid.NewGuid();

    public DocumentClassifierTests()
    {
        _db = TestAppDbContext.Create();
        _providerResolver = Substitute.For<IAiProviderResolver>();
        _classificationService = Substitute.For<IAiClassificationService>();
        _searchVectorUpdater = Substitute.For<ISearchVectorUpdater>();
        _sut = new DocumentClassifier(_db, _providerResolver, _classificationService, _searchVectorUpdater);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ClassifyAsync_ResolvesProvider_LoadsInputs_CallsAiService()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        var provider = new AiProvider
        {
            Id = _providerId,
            Name = "Test Provider",
            ProviderType = AiProviderType.AzureAIFoundry,
            Configuration = "{}",
            IsEnabled = true
        };

        SeedDocumentWithVersion("extracted text here");
        SeedTag(tagId, "Finance", "Finance");

        _providerResolver.ResolveAsync(null, ct).Returns(provider);
        _classificationService.ClassifyAsync(
            provider, "Test Doc", "extracted text here",
            Arg.Any<List<TagInfo>>(), ct)
            .Returns(new ClassificationResult([tagId], "A summary", "Better Title"));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ct);

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

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ct);

        Assert.Null(result);
        await _classificationService.DidNotReceive().ClassifyAsync(
            Arg.Any<AiProvider>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AiServiceReturnsNull_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var provider = new AiProvider
        {
            Id = _providerId,
            Name = "Test Provider",
            ProviderType = AiProviderType.AzureAIFoundry,
            Configuration = "{}",
            IsEnabled = true
        };

        SeedDocumentWithVersion("some text");

        _providerResolver.ResolveAsync(null, ct).Returns(provider);
        _classificationService.ClassifyAsync(
            provider, "Test Doc", "some text",
            Arg.Any<List<TagInfo>>(), ct)
            .Returns((ClassificationResult?)null);

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClassifyAsync_UsesLatestVersion()
    {
        var ct = TestContext.Current.CancellationToken;

        var provider = new AiProvider
        {
            Id = _providerId,
            Name = "Test Provider",
            ProviderType = AiProviderType.AzureAIFoundry,
            Configuration = "{}",
            IsEnabled = true
        };

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
            Arg.Any<List<TagInfo>>(), ct)
            .Returns(new ClassificationResult([], "summary", "title"));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", null, ct);

        Assert.NotNull(result);
        await _classificationService.Received(1).ClassifyAsync(
            provider, "Test Doc", "latest text",
            Arg.Any<List<TagInfo>>(), ct);
    }

    [Fact]
    public async Task ClassifyAndApplyAsync_AppliesClassification()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        SeedProvider();

        _providerResolver.ResolveAsync(null, ct).Returns(new AiProvider
        {
            Id = _providerId,
            Name = "Test Provider",
            ProviderType = AiProviderType.AzureAIFoundry,
            Configuration = "{}",
            IsEnabled = true
        });
        _classificationService.ClassifyAsync(
            Arg.Any<AiProvider>(), "Test Doc", "some text",
            Arg.Any<List<TagInfo>>(), ct)
            .Returns(new ClassificationResult([tagId], "AI summary", "AI Title"));

        var result = await _sut.ClassifyAndApplyAsync(_documentId, null, ct);

        Assert.True(result);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("AI Title", doc!.Name);
        Assert.Equal("AI summary", doc.Summary);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Name);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Tags);

        await _searchVectorUpdater.Received(1).UpdateSearchVectorAsync(_documentId, ct);
    }

    [Fact]
    public async Task ClassifyAndApplyAsync_NoProvider_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithVersion("some text");

        _providerResolver.ResolveAsync(null, ct).Returns((AiProvider?)null);

        var result = await _sut.ClassifyAndApplyAsync(_documentId, null, ct);

        Assert.False(result);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("Test Doc", doc!.Name);
        Assert.Null(doc.Summary);

        await _searchVectorUpdater.DidNotReceive().UpdateSearchVectorAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAndApplyAsync_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ClassifyAndApplyAsync(Guid.NewGuid(), null, ct));
    }

    [Fact]
    public async Task ClassifyAndApplyAsync_ClearsPendingSuggestions_BeforeApplyingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedProvider();
        SeedTag(oldTagId, "OldTag", "OldTag");
        SeedTag(newTagId, "NewTag", "NewTag");

        _db.DocumentTags.Add(new DocumentTag
        {
            DocumentId = _documentId,
            TagId = oldTagId
        });
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

        _providerResolver.ResolveAsync(null, ct).Returns(new AiProvider
        {
            Id = _providerId,
            Name = "Test Provider",
            ProviderType = AiProviderType.AzureAIFoundry,
            Configuration = "{}",
            IsEnabled = true
        });
        _classificationService.ClassifyAsync(
            Arg.Any<AiProvider>(), "Test Doc", "some text",
            Arg.Any<List<TagInfo>>(), ct)
            .Returns(new ClassificationResult([newTagId], "new summary", "New Title"));

        await _sut.ClassifyAndApplyAsync(_documentId, null, ct);

        var pendingSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();

        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == oldTagId.ToString());
        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == "Old AI Name");
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == newTagId.ToString());
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == "New Title");
    }

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

    private void SeedProvider()
    {
        _db.AiProviders.Add(new AiProvider
        {
            Id = _providerId,
            Name = "Test Provider",
            ProviderType = AiProviderType.AzureAIFoundry,
            Configuration = "{}",
            IsEnabled = true
        });
        _db.SaveChanges();
    }
}
