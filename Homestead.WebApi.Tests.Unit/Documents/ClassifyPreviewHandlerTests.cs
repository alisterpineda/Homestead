using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;
using Homestead.WebApi.Modules.Documents.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassifyPreviewHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly ClassifyPreviewHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();

    public ClassifyPreviewHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _classifier = Substitute.For<IDocumentClassifier>();
        _sut = new ClassifyPreviewHandler(_db, _classifier);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_ReturnsHydratedResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Tags.Add(new Tag
        {
            Id = tagId,
            Name = "Finance",
            Path = "Finance",
            Color = "#ff0000",
            EffectiveColor = "#ff0000",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var availableTags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var classification = new ClassificationResult([tagId], [], "A summary", "Better Title");
        var output = new DocumentClassificationOutput(classification, availableTags, Guid.NewGuid());

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct).Returns(output);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Equal("Better Title", response.SuggestedTitle);
        Assert.Null(response.Summary); // Summary is never part of the classify scope
        Assert.Single(response.Tags);
        Assert.Equal(tagId.ToString(), response.Tags[0].Id.ToString());
        Assert.Equal("Finance", response.Tags[0].Name);
        Assert.Equal("Finance", response.Tags[0].Path);
        Assert.Equal("#ff0000", response.Tags[0].Color);
        Assert.Equal("#ff0000", response.Tags[0].EffectiveColor);
    }

    [Fact]
    public async Task Handle_NoResult_ReturnsEmptyResponse()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct)
            .Returns((DocumentClassificationOutput?)null);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Null(response.SuggestedTitle);
        Assert.Null(response.Summary);
        Assert.Empty(response.Tags);
        Assert.Empty(response.Parties);
    }

    [Fact]
    public async Task Handle_FiltersUnmatchedTagIds()
    {
        var ct = TestContext.Current.CancellationToken;
        var knownTagId = Guid.NewGuid();
        var unknownTagId = Guid.NewGuid();

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Tags.Add(new Tag
        {
            Id = knownTagId,
            Name = "Finance",
            Path = "Finance",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var availableTags = new List<TagInfo> { new(knownTagId, "Finance", "Finance") };
        var classification = new ClassificationResult([knownTagId, unknownTagId], [], "summary", "title");
        var output = new DocumentClassificationOutput(classification, availableTags, Guid.NewGuid());

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct).Returns(output);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Single(response.Tags);
        Assert.Equal(knownTagId, response.Tags[0].Id);
    }

    [Fact]
    public async Task Handle_ExcludesTitleMatchingCurrentName()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Same Name",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var classification = new ClassificationResult([], [], "new summary", "Same Name");
        var output = new DocumentClassificationOutput(classification, [], Guid.NewGuid());

        _classifier.ClassifyAsync(_documentId, "Same Name", null, Arg.Any<ClassificationScope>(), ct).Returns(output);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Null(response.SuggestedTitle);
        Assert.Null(response.Summary); // Summary is never part of the classify scope
    }

    [Fact]
    public async Task Handle_AlwaysReturnsNullSummary_EvenWhenClassificationHasOne()
    {
        // Summary is never in the classify scope — the preview response must always omit it,
        // regardless of what the (mocked) classifier returns. Summarize is a separate action.
        var ct = TestContext.Current.CancellationToken;

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var classification = new ClassificationResult([], [], "a fresh summary", "New Title");
        var output = new DocumentClassificationOutput(classification, [], Guid.NewGuid());

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct).Returns(output);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Equal("New Title", response.SuggestedTitle);
        Assert.Null(response.Summary);
    }

    [Fact]
    public async Task Handle_FieldsNull_PassesClassifyScope_WithoutSummary()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct)
            .Returns((DocumentClassificationOutput?)null);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId, Fields = null };
        await _sut.Handle(query, ct);

        await _classifier.Received(1).ClassifyAsync(
            _documentId, "Test Doc", null,
            ClassificationScope.Tags | ClassificationScope.Parties | ClassificationScope.Name, ct);
    }

    [Fact]
    public async Task Handle_FieldsTags_PassesTagsScopeOnly()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct)
            .Returns((DocumentClassificationOutput?)null);

        var query = new ClassifyPreviewQuery
        {
            DocumentId = _documentId,
            Fields = [DocumentSuggestionField.Tags]
        };
        await _sut.Handle(query, ct);

        await _classifier.Received(1).ClassifyAsync(
            _documentId, "Test Doc", null, ClassificationScope.Tags, ct);
    }

    [Fact]
    public async Task Handle_ExcludesTagsAlreadyOnDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var existingTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        var doc = new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Documents.Add(doc);
        _db.Tags.Add(new Tag
        {
            Id = existingTagId, Name = "Existing", Path = "Existing",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.Tags.Add(new Tag
        {
            Id = newTagId, Name = "New", Path = "New",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentTags.Add(new DocumentTag
        {
            DocumentId = _documentId,
            TagId = existingTagId
        });
        await _db.SaveChangesAsync(ct);

        var availableTags = new List<TagInfo>
        {
            new(existingTagId, "Existing", "Existing"),
            new(newTagId, "New", "New")
        };
        var classification = new ClassificationResult([existingTagId, newTagId], [], "summary", "title");
        var output = new DocumentClassificationOutput(classification, availableTags, Guid.NewGuid());

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct).Returns(output);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Single(response.Tags);
        Assert.Equal(newTagId, response.Tags[0].Id);
    }

    [Fact]
    public async Task Handle_ReturnsMatchedParties_ExcludingThoseAlreadyLinked()
    {
        var ct = TestContext.Current.CancellationToken;
        var existingPartyId = Guid.NewGuid();
        var newPartyId = Guid.NewGuid();

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.Parties.Add(new Homestead.WebApi.Domain.Parties.Party
        {
            Id = existingPartyId, Name = "Existing Party",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.Parties.Add(new Homestead.WebApi.Domain.Parties.Party
        {
            Id = newPartyId, Name = "New Party",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentParties.Add(new Homestead.WebApi.Domain.Parties.DocumentParty
        {
            DocumentId = _documentId,
            PartyId = existingPartyId
        });
        await _db.SaveChangesAsync(ct);

        var classification = new ClassificationResult([], [existingPartyId, newPartyId], "summary", "title");
        var output = new DocumentClassificationOutput(classification, [], Guid.NewGuid());

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, Arg.Any<ClassificationScope>(), ct).Returns(output);

        var query = new ClassifyPreviewQuery { DocumentId = _documentId };
        var response = await _sut.Handle(query, ct);

        Assert.Single(response.Parties);
        Assert.Equal(newPartyId, response.Parties[0].Id);
        Assert.Equal("New Party", response.Parties[0].Name);
    }
}
