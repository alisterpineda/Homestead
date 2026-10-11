using System.Net;
using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassifyDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly IUnitOfWork _unitOfWork;
    private bool _inTransaction;
    private bool _handledInTransaction;
    private readonly IApplyClassificationHandler _applyClassification;
    private readonly IProcessingNotifier _notifier;
    private readonly ClassifyDocumentJobHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();
    private const string DocumentName = "Test Doc";

    public ClassifyDocumentJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = DocumentName,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();

        _classifier = Substitute.For<IDocumentClassifier>();
        _applyClassification = Substitute.For<IApplyClassificationHandler>();
        _applyClassification.HandleAsync(Arg.Any<ApplyClassificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ApplyClassificationResult(DocumentName));
        // Run the work handed to the transaction, so the command handler is reached through it, and record
        // whether the command handler ran while that work was in progress.
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<ApplyClassificationResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _inTransaction = true;
                try { return await call.Arg<Func<Task<ApplyClassificationResult>>>()(); }
                finally { _inTransaction = false; }
            });
        _applyClassification.When(x => x.HandleAsync(Arg.Any<ApplyClassificationCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => _handledInTransaction = _inTransaction);
        _notifier = Substitute.For<IProcessingNotifier>();
        _sut = new ClassifyDocumentJobHandler(_db, _classifier, _notifier, _unitOfWork, _applyClassification);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OptsIntoRetries_ForTransientFailuresAndMalformedModelOutput()
    {
        IJobHandler handler = _sut;

        Assert.Equal(3, handler.MaxAttempts);
        Assert.True(handler.ShouldRetry(new JsonException("Unexpected end of JSON")));
        Assert.True(handler.ShouldRetry(new HttpRequestException("Bad gateway", null, HttpStatusCode.BadGateway)));

        // A document that isn't there fails the same way every time.
        Assert.False(handler.ShouldRetry(new InvalidOperationException("Document not found")));
    }

    [Fact]
    public async Task APayloadThatWontParse_FailsWithoutARetry()
    {
        var ct = TestContext.Current.CancellationToken;
        IJobHandler handler = _sut;
        var job = CreateJob();
        job.Payload = "{ not json";

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => _sut.HandleAsync(job, ct));

        // Must not surface as the JsonException that ShouldRetry reads as malformed model output.
        Assert.False(handler.ShouldRetry(thrown));
    }

    [Fact]
    public async Task HandleAsync_ClassifiesInJobScope_UsingDocumentName()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        // The slow AI call runs here in the job scope (before the command transaction), keyed off the
        // document name read from the store.
        await _classifier.Received(1).ClassifyAsync(
            _documentId, DocumentName, ClassificationScope.All, ct);
    }

    [Fact]
    public async Task HandleAsync_AppliesClassificationCommand_WithPrecomputedResult_AndActorFromJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var classification = new ClassificationResult([], [], "summary", DocumentName);
        _classifier.ClassifyAsync(_documentId, DocumentName, Arg.Any<ClassificationScope>(), ct)
            .Returns(new DocumentClassificationOutput(classification, []));

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        await _applyClassification.Received(1).HandleAsync(
            Arg.Is<ApplyClassificationCommand>(c =>
                c.DocumentId == _documentId
                && c.Classification == classification
                && c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ClassifiesFirst_ThenSetsActorFromJob_ThenAppliesInsideTheTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        // The slow AI call holds no transaction. The actor is set before the transaction opens, so any
        // flush inside the command is attributed to whoever enqueued the job; the command runs inside it.
        Received.InOrder(() =>
        {
            _classifier.ClassifyAsync(_documentId, DocumentName, ClassificationScope.All, ct);
            _unitOfWork.SetActor(job.CreatedByUserId);
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<ApplyClassificationResult>>>(), ct);
        });
        Assert.True(_handledInTransaction);
    }

    [Fact]
    public async Task HandleAsync_NoClassificationProduced_AppliesCommandWithNullClassification()
    {
        var ct = TestContext.Current.CancellationToken;
        // Default substitute returns null output (AI unconfigured / nothing produced).
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        await _applyClassification.Received(1).HandleAsync(
            Arg.Is<ApplyClassificationCommand>(c =>
                c.DocumentId == _documentId
                && c.Classification == null),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ForwardsScopeFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(scope: ClassificationScope.Summary);

        await _sut.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAsync(
            _documentId, DocumentName, ClassificationScope.Summary, ct);
        await _applyClassification.Received(1).HandleAsync(
            Arg.Is<ApplyClassificationCommand>(c => c.Scope == ClassificationScope.Summary), ct);
    }

    [Fact]
    public async Task HandleAsync_SendsNotification_WhenNoParentJob_UsesResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        _applyClassification.HandleAsync(Arg.Any<ApplyClassificationCommand>(), ct)
            .Returns(new ApplyClassificationResult("AI Suggested Name"));

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            _documentId,
            "AI Suggested Name",
            error: null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_SkipsNotification_WhenChildJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob();
        job.ParentJobId = Guid.NewGuid();

        await _sut.HandleAsync(job, ct);

        // Still applies the classification...
        await _applyClassification.Received(1).HandleAsync(Arg.Any<ApplyClassificationCommand>(), ct);
        // ...but leaves notification to the parent's progress updates.
        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(),
            Arg.Any<ProcessingEntityType>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SetsEntityId()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        Assert.Equal(_documentId, job.EntityId);
    }

    [Fact]
    public async Task HandleAsync_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = new ClassifyDocumentPayload(Guid.NewGuid());
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ClassifyDocument,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        job.SetPayload(payload);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));
    }

    [Fact]
    public async Task HandleAsync_CommandThrows_Propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        _applyClassification.HandleAsync(Arg.Any<ApplyClassificationCommand>(), ct)
            .Returns<ApplyClassificationResult>(_ => throw new InvalidOperationException("apply failed."));

        var job = CreateJob();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));
    }

    private ProcessingJob CreateJob(ClassificationScope scope = ClassificationScope.All)
    {
        var payload = new ClassifyDocumentPayload(_documentId, scope);
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ClassifyDocument,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        job.SetPayload(payload);
        return job;
    }
}
