using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.AiClassification;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassifyDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly IProcessingNotifier _notifier;
    private readonly ClassifyDocumentJobHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Guid _providerId = Guid.NewGuid();

    public ClassifyDocumentJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _classifier = Substitute.For<IDocumentClassifier>();
        _searchVectorUpdater = Substitute.For<ISearchVectorUpdater>();
        _notifier = Substitute.For<IProcessingNotifier>();
        _sut = new ClassifyDocumentJobHandler(_db, _classifier, _searchVectorUpdater, _notifier);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HandleAsync_CallsDocumentClassifier_AppliesResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithProvider();
        SeedTag(tagId, "Finance", "Finance");

        var classification = new ClassificationResult([tagId], "AI summary", "AI Title");
        var availableTags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var output = new DocumentClassificationOutput(classification, availableTags, _providerId);

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, ct).Returns(output);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("AI Title", doc!.Name);
        Assert.Equal("AI summary", doc.Summary);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Equal(3, suggestions.Count);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Name);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Summary);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Tags);
    }

    [Fact]
    public async Task HandleAsync_NullResult_SkipsApply()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithProvider();

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, ct)
            .Returns((DocumentClassificationOutput?)null);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("Test Doc", doc!.Name);
        Assert.Null(doc.Summary);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Empty(suggestions);

        await _searchVectorUpdater.DidNotReceive().UpdateSearchVectorAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ClearsPendingSuggestions_BeforeApplyingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        SeedDocumentWithProvider();
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

        var classification = new ClassificationResult([newTagId], "new summary", "New Title");
        var availableTags = new List<TagInfo>
        {
            new(oldTagId, "OldTag", "OldTag"),
            new(newTagId, "NewTag", "NewTag")
        };
        var output = new DocumentClassificationOutput(classification, availableTags, _providerId);

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, ct).Returns(output);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        var pendingSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();

        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == oldTagId.ToString());
        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == "Old AI Name");
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == newTagId.ToString());
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == "New Title");
    }

    private void SeedDocumentWithProvider()
    {
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
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

    private ProcessingJob CreateJob()
    {
        var payload = new ClassifyDocumentPayload(_documentId);
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ClassifyDocument,
            Status = ProcessingJobStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow
        };
        job.SetPayload(payload);
        return job;
    }
}
