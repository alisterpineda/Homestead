using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassifyDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentClassifier _classifier;
    private readonly IProcessingNotifier _notifier;
    private readonly ClassifyDocumentJobHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();

    public ClassifyDocumentJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _classifier = Substitute.For<IDocumentClassifier>();
        _notifier = Substitute.For<IProcessingNotifier>();
        _sut = new ClassifyDocumentJobHandler(_db, _classifier, _notifier);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HandleAsync_CallsClassifyAndApplyAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(true);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct);
    }

    [Fact]
    public async Task HandleAsync_PassesAiProviderIdFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var aiProviderId = Guid.NewGuid();

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, aiProviderId, Arg.Any<ClassificationScope>(), null, ct).Returns(true);

        var job = CreateJob(aiProviderId);
        await _sut.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAndApplyAsync(_documentId, aiProviderId, Arg.Any<ClassificationScope>(), null, ct);
    }

    [Fact]
    public async Task HandleAsync_SendsNotification_WhenNoParentJob()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(true);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            _documentId,
            "Test Doc",
            null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_SkipsNotification_WhenChildJob()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(true);

        var job = CreateJob();
        job.ParentJobId = Guid.NewGuid();
        await _sut.HandleAsync(job, ct);

        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(),
            Arg.Any<ProcessingEntityType>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UsesCurrentDocumentName_ForNotification()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct)
            .Returns(callInfo =>
            {
                var doc = _db.Documents.Find(_documentId)!;
                doc.Name = "AI Suggested Name";
                _db.SaveChanges();
                return true;
            });

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
    public async Task HandleAsync_SetsEntityId()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(true);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        Assert.Equal(_documentId, job.EntityId);
    }

    [Fact]
    public async Task HandleAsync_ClassificationSkipped_StillSendsNotification()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(false);

        var job = CreateJob();
        await _sut.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            _documentId,
            "Test Doc",
            null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_ClassificationSkipped_ChildJob_CompletesWithoutNotification()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(false);

        var job = CreateJob();
        job.ParentJobId = Guid.NewGuid();
        await _sut.HandleAsync(job, ct);

        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(),
            Arg.Any<ProcessingEntityType>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForwardsScopeFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocument();

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct).Returns(true);

        var job = CreateJob(scope: ClassificationScope.Summary);
        await _sut.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAndApplyAsync(
            _documentId, null, ClassificationScope.Summary, null, ct);
    }

    [Fact]
    public async Task HandleAsync_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        _classifier.ClassifyAndApplyAsync(_documentId, null, Arg.Any<ClassificationScope>(), null, ct)
            .Returns<bool>(x => throw new InvalidOperationException("Document not found."));

        var job = CreateJob();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HandleAsync(job, ct));
    }

    private void SeedDocument()
    {
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }

    private ProcessingJob CreateJob(Guid? aiProviderId = null, ClassificationScope scope = ClassificationScope.All)
    {
        var payload = new ClassifyDocumentPayload(_documentId, aiProviderId, scope);
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
