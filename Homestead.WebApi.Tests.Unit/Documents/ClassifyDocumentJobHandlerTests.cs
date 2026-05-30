using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Processing;
using MediatR;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassifyDocumentJobHandlerTests
{
    private readonly ISender _sender;
    private readonly IProcessingNotifier _notifier;
    private readonly ClassifyDocumentJobHandler _sut;

    private readonly Guid _documentId = Guid.NewGuid();

    public ClassifyDocumentJobHandlerTests()
    {
        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<ApplyClassificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ApplyClassificationResult("Test Doc"));
        _notifier = Substitute.For<IProcessingNotifier>();
        _sut = new ClassifyDocumentJobHandler(_notifier, _sender);
    }

    [Fact]
    public async Task HandleAsync_SendsApplyClassificationCommand_WithActorFromJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob();

        await _sut.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ApplyClassificationCommand>(c =>
                c.DocumentId == _documentId
                && c.AiProviderId == null
                && c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_PassesAiProviderIdFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var aiProviderId = Guid.NewGuid();
        var job = CreateJob(aiProviderId);

        await _sut.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ApplyClassificationCommand>(c => c.AiProviderId == aiProviderId), ct);
    }

    [Fact]
    public async Task HandleAsync_ForwardsScopeFromPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(scope: ClassificationScope.Summary);

        await _sut.HandleAsync(job, ct);

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
    public async Task HandleAsync_CommandThrows_Propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        _sender.Send(Arg.Any<ApplyClassificationCommand>(), ct)
            .Returns<ApplyClassificationResult>(_ => throw new InvalidOperationException("Document not found."));

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
