using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;
using Homestead.WebApi.Processing;
using NSubstitute;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ReprocessDocumentVersionHandlerTests : IDisposable
{
    private readonly TextExtractionEngineResolver _resolver;
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IJobQueue _jobQueue;
    private readonly ReprocessDocumentVersionHandler _sut;

    private readonly Guid _docId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ReprocessDocumentVersionHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _jobQueue = Substitute.For<IJobQueue>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        _resolver = new TextExtractionEngineResolver(TestTextExtraction.Engines(), defaultEngineKey: null);
        _sut = new ReprocessDocumentVersionHandler(_db, _currentUser, _resolver, _jobQueue);

        SeedDocumentAndVersion();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDocumentAndVersion()
    {
        _db.Documents.Add(new Document
        {
            Id = _docId,
            Name = "Test.pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = _versionId,
            DocumentId = _docId,
            VersionNumber = 1,
            ContentType = "application/pdf",
            FileSize = 1024,
            FilePath = "documents/files/abcd/abcdef.pdf",
            FileHash = "abcdef",
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Handle_Succeeds_EnqueuesJob()
    {
        var request = new ReprocessDocumentVersionCommand
        {
            DocumentId = _docId,
            VersionId = _versionId,
        };

        var result = await _sut.Handle(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task Handle_NonExistentDocument_ThrowsNotFound()
    {
        var request = new ReprocessDocumentVersionCommand
        {
            DocumentId = Guid.NewGuid(),
            VersionId = _versionId,
        };

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_NonExistentVersion_ThrowsNotFound()
    {
        var request = new ReprocessDocumentVersionCommand
        {
            DocumentId = _docId,
            VersionId = Guid.NewGuid(),
        };

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(request, TestContext.Current.CancellationToken));
    }

    // Captures the payload the handler enqueues.
    private List<string> CapturePayloads()
    {
        var payloads = new List<string>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = new EnqueueJobRequest();
                callInfo.Arg<Action<EnqueueJobRequest>?>()?.Invoke(req);
                payloads.Add(req.Payload!);
                return Guid.NewGuid();
            });
        return payloads;
    }

    // The key is resolved once here and stamped, so the job path needs no settings read and the run is
    // deterministic regardless of what the configured default does afterwards.
    [Fact]
    public async Task Handle_ExplicitEngineKey_IsStampedIntoThePayload()
    {
        var payloads = CapturePayloads();

        await _sut.Handle(new ReprocessDocumentVersionCommand
        {
            DocumentId = _docId,
            VersionId = _versionId,
            EngineKey = TestTextExtraction.DefaultKey
        }, TestContext.Current.CancellationToken);

        Assert.Contains($"\"EngineKey\":\"{TestTextExtraction.DefaultKey}\"", Assert.Single(payloads));
    }

    [Fact]
    public async Task Handle_NoEngineKey_StampsTheHouseholdDefault()
    {
        var payloads = CapturePayloads();

        await _sut.Handle(new ReprocessDocumentVersionCommand
        {
            DocumentId = _docId,
            VersionId = _versionId
        }, TestContext.Current.CancellationToken);

        Assert.Contains($"\"EngineKey\":\"{TestTextExtraction.DefaultKey}\"", Assert.Single(payloads));
    }

    // Rejected synchronously, before a job row exists: a user who picked azure-layout must not get
    // Tesseract's output back under a completed job.
    [Fact]
    public async Task Handle_UnavailableExplicitEngineKey_ThrowsAndEnqueuesNothing()
    {
        var request = new ReprocessDocumentVersionCommand
        {
            DocumentId = _docId,
            VersionId = _versionId,
            EngineKey = "azure-layout"
        };

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(request, TestContext.Current.CancellationToken));

        await _jobQueue.DidNotReceive().EnqueueAsync(
            Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>());
    }
}
