using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Processing;
using MediatR;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassifyDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly ISender _sender;
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
        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<ApplyClassificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ApplyClassificationResult(DocumentName));
        _notifier = Substitute.For<IProcessingNotifier>();
        _sut = new ClassifyDocumentJobHandler(_db, _classifier, _notifier, _sender);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
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
            _documentId, DocumentName, null, ClassificationScope.All, ct);
    }

    [Fact]
    public async Task HandleAsync_SendsApplyClassificationCommand_WithPrecomputedResult_AndActorFromJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerId = Guid.NewGuid();
        var classification = new ClassificationResult([], [], "summary", DocumentName);
        _classifier.ClassifyAsync(_documentId, DocumentName, Arg.Any<Guid?>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new DocumentClassificationOutput(classification, [], providerId));

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ApplyClassificationCommand>(c =>
                c.DocumentId == _documentId
                && c.Classification == classification
                && c.ProviderId == providerId
                && c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_NoClassificationProduced_SendsCommandWithNulls()
    {
        var ct = TestContext.Current.CancellationToken;
        // Default substitute returns null output (no provider / nothing produced).
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ApplyClassificationCommand>(c =>
                c.DocumentId == _documentId
                && c.Classification == null
                && c.ProviderId == null),
            ct);
    }

    [Fact]
    public async Task HandleAsync_PassesAiProviderIdFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var aiProviderId = Guid.NewGuid();
        var job = CreateJob(aiProviderId);

        await _sut.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAsync(
            _documentId, DocumentName, aiProviderId, ClassificationScope.All, ct);
    }

    [Fact]
    public async Task HandleAsync_ForwardsScopeFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(scope: ClassificationScope.Summary);

        await _sut.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAsync(
            _documentId, DocumentName, null, ClassificationScope.Summary, ct);
        await _sender.Received(1).Send(
            Arg.Is<ApplyClassificationCommand>(c => c.Scope == ClassificationScope.Summary), ct);
    }

    [Fact]
    public async Task HandleAsync_SendsNotification_WhenNoParentJob_UsesResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        _sender.Send(Arg.Any<ApplyClassificationCommand>(), ct)
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
        await _sender.Received(1).Send(Arg.Any<ApplyClassificationCommand>(), ct);
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
        _sender.Send(Arg.Any<ApplyClassificationCommand>(), ct)
            .Returns<ApplyClassificationResult>(_ => throw new InvalidOperationException("apply failed."));

        var job = CreateJob();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(job, ct));
    }

    private ProcessingJob CreateJob(Guid? aiProviderId = null, ClassificationScope scope = ClassificationScope.All)
    {
        var payload = new ClassifyDocumentPayload(_documentId, aiProviderId, scope);
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
