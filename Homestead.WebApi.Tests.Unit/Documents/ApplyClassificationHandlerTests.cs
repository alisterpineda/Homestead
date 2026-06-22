using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ApplyClassificationHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly AuditContext _audit = new();
    private readonly ApplyClassificationHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();

    public ApplyClassificationHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _db.Documents.Add(new Document { Id = _documentId, Name = "Doc", CreatedAt = DateTimeOffset.UtcNow });
        _db.SaveChanges();

        _classifier = Substitute.For<IDocumentClassifier>();
        _sut = new ApplyClassificationHandler(_db, _classifier, _audit);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_AppliesPrecomputedClassification_WhenProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorId = Guid.NewGuid();
        var classification = new ClassificationResult([], [], "summary", "Doc");

        var result = await _sut.Handle(new ApplyClassificationCommand
        {
            DocumentId = _documentId,
            Classification = classification,
            Scope = ClassificationScope.All,
            ActorUserId = actorId
        }, ct);

        Assert.Equal("Doc", result.DocumentName);
        // The handler does NOT re-run ClassifyAsync — only ApplyAsync with the precomputed result.
        await _classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());
        await _classifier.Received(1).ApplyAsync(
            Arg.Is<Document>(d => d.Id == _documentId),
            classification, ClassificationScope.All, actorId, true, ct);
        Assert.True(_audit.HasEntries);   // tracked for the Updated audit entry
    }

    [Fact]
    public async Task Handle_IsNoOp_WhenClassificationNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.Handle(new ApplyClassificationCommand
        {
            DocumentId = _documentId,
            Classification = null,
            Scope = ClassificationScope.All
        }, ct);

        Assert.Equal("Doc", result.DocumentName);
        await _classifier.DidNotReceive().ApplyAsync(
            Arg.Any<Document>(), Arg.Any<ClassificationResult>(),
            Arg.Any<ClassificationScope>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.False(_audit.HasEntries);
    }

    [Fact]
    public async Task Handle_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.Handle(new ApplyClassificationCommand
        {
            DocumentId = Guid.NewGuid(),
            Classification = new ClassificationResult([], [], "s", "t")
        }, ct));
    }
}
