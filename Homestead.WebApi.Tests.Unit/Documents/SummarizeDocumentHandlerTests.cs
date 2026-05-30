using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;
using Homestead.WebApi.Modules.Documents.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class SummarizeDocumentHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly SummarizeDocumentHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public SummarizeDocumentHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _classifier = Substitute.For<IDocumentClassifier>();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _sut = new SummarizeDocumentHandler(_db, _classifier, _audit, _unitOfWork, _currentUser);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedDocument(string? summary = null)
    {
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            Summary = summary,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Handle_Success_WritesSummary_Returns_Tracks()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument();

        var output = new DocumentClassificationOutput(
            new ClassificationResult([], [], "Generated summary", "Test Doc"), [], Guid.NewGuid());
        _classifier.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.Summary, ct)
            .Returns(output);

        var response = await _sut.Handle(
            new SummarizeDocumentCommand { DocumentId = _documentId }, ct);

        Assert.Equal("Generated summary", response.Summary);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("Generated summary", doc!.Summary);
        Assert.Equal(_userId, doc.UpdatedByUserId);

        await _unitOfWork.Received(1).SaveChangesAsync(ct);
        _audit.Received(1).Track<DocumentSnapshot, Document>(
            Arg.Any<Document>(), "Document", _documentId);
    }

    [Fact]
    public async Task Handle_NullClassification_LeavesSummaryUnchanged_NoThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocument(summary: "existing summary");

        _classifier.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.Summary, ct)
            .Returns((DocumentClassificationOutput?)null);

        var response = await _sut.Handle(
            new SummarizeDocumentCommand { DocumentId = _documentId }, ct);

        Assert.Equal("existing summary", response.Summary);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("existing summary", doc!.Summary);
    }

    [Fact]
    public async Task Handle_BlankSummary_LeavesExistingSummaryUnchanged_NoThrow()
    {
        // A non-null result whose Summary is blank (model omitted/returned empty) must NOT
        // overwrite an existing good summary — graceful no-op, no data loss.
        var ct = TestContext.Current.CancellationToken;
        SeedDocument(summary: "existing summary");

        var output = new DocumentClassificationOutput(
            new ClassificationResult([], [], "   ", "Test Doc"), [], Guid.NewGuid());
        _classifier.ClassifyAsync(_documentId, "Test Doc", null, ClassificationScope.Summary, ct)
            .Returns(output);

        var response = await _sut.Handle(
            new SummarizeDocumentCommand { DocumentId = _documentId }, ct);

        Assert.Equal("existing summary", response.Summary);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("existing summary", doc!.Summary);
    }

    [Fact]
    public async Task Handle_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(new SummarizeDocumentCommand { DocumentId = Guid.NewGuid() }, ct));
    }
}
