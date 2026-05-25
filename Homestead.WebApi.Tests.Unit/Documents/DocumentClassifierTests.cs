using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.AiClassification;
using Homestead.WebApi.Modules.Documents.Services;
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
}
