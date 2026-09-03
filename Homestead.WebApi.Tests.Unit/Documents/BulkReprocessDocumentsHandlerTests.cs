using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;
using Homestead.WebApi.Processing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class BulkReprocessDocumentsHandlerTests : IDisposable
{
    private readonly TextExtractionEngineResolver _resolver;
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDocumentQueryService _queryService;
    private readonly IJobQueue _jobQueue;
    private readonly BulkReprocessDocumentsHandler _sut;

    private readonly Guid _userId = Guid.NewGuid();

    public BulkReprocessDocumentsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _queryService = Substitute.For<IDocumentQueryService>();
        _jobQueue = Substitute.For<IJobQueue>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        _resolver = new TextExtractionEngineResolver(TestTextExtraction.Engines(), defaultEngineKey: null);
        _sut = new BulkReprocessDocumentsHandler(_db, _currentUser, _queryService, _resolver, _jobQueue);
    }

    public void Dispose() => _db.Dispose();

    private (Guid DocId, Guid VersionId) SeedDocument(string name = "Test.pdf", int versionCount = 1)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Guid latestVersionId = Guid.Empty;
        for (var i = 1; i <= versionCount; i++)
        {
            var versionId = Guid.NewGuid();
            _db.DocumentVersions.Add(new DocumentVersion
            {
                Id = versionId,
                DocumentId = docId,
                VersionNumber = i,
                ContentType = "application/pdf",
                FileSize = 1024,
                FilePath = $"documents/files/{versionId}.pdf",
                FileHash = $"hash-{versionId}",
                FileExtension = ".pdf",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            latestVersionId = versionId;
        }

        _db.SaveChanges();
        return (docId, latestVersionId);
    }

    [Fact]
    public async Task Handle_MultipleDocuments_ReturnsSingleParentJobId()
    {
        var (docId1, _) = SeedDocument("Doc1.pdf");
        var (docId2, _) = SeedDocument("Doc2.pdf");

        var result = await _sut.Handle(
            new BulkReprocessDocumentsCommand([docId1, docId2]),
            CancellationToken.None);

        Assert.Single(result.JobIds);
        Assert.NotEqual(Guid.Empty, result.JobIds[0]);
    }

    [Fact]
    public async Task Handle_EnqueuesSingleBulkReprocessParentJob()
    {
        var (docId1, _) = SeedDocument("Doc1.pdf");
        var (docId2, _) = SeedDocument("Doc2.pdf");

        await _sut.Handle(
            new BulkReprocessDocumentsCommand([docId1, docId2]),
            CancellationToken.None);

        await _jobQueue.Received(1).EnqueueAsync(
            ProcessingJobType.BulkReprocessDocuments,
            Arg.Any<Action<EnqueueJobRequest>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PayloadContainsDocumentIds()
    {
        var (docId1, _) = SeedDocument("Doc1.pdf");
        var (docId2, _) = SeedDocument("Doc2.pdf");

        var capturedPayloads = new List<string>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<EnqueueJobRequest>?>();
                var req = new EnqueueJobRequest();
                configure?.Invoke(req);
                capturedPayloads.Add(req.Payload!);
                return Guid.NewGuid();
            });

        await _sut.Handle(
            new BulkReprocessDocumentsCommand([docId1, docId2]),
            CancellationToken.None);

        Assert.Single(capturedPayloads);
        Assert.Contains(docId1.ToString(), capturedPayloads[0]);
        Assert.Contains(docId2.ToString(), capturedPayloads[0]);
    }

    [Fact]
    public async Task Handle_EngineKeyIsResolvedOnceIntoTheParentPayload()
    {
        var (docId, _) = SeedDocument("Doc1.pdf");

        var capturedPayloads = new List<string>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = new EnqueueJobRequest();
                callInfo.Arg<Action<EnqueueJobRequest>?>()?.Invoke(req);
                capturedPayloads.Add(req.Payload!);
                return Guid.NewGuid();
            });

        await _sut.Handle(
            new BulkReprocessDocumentsCommand([docId], TestTextExtraction.DefaultKey),
            CancellationToken.None);

        Assert.Contains($"\"EngineKey\":\"{TestTextExtraction.DefaultKey}\"", Assert.Single(capturedPayloads));
    }

    // 400 before a job row exists — the alternative is a batch of N children silently running the engine
    // the caller explicitly rejected.
    [Fact]
    public async Task Handle_UnavailableExplicitEngineKey_ThrowsAndEnqueuesNothing()
    {
        var (docId, _) = SeedDocument("Doc1.pdf");

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Handle(
            new BulkReprocessDocumentsCommand([docId], "azure-layout"), CancellationToken.None));

        await _jobQueue.DidNotReceive().EnqueueAsync(
            Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoMatchingDocuments_ReturnsEmptyJobIds()
    {
        var result = await _sut.Handle(
            new BulkReprocessDocumentsCommand([Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Empty(result.JobIds);
    }

    [Fact]
    public async Task Handle_Succeeds_EnqueuesJob()
    {
        var (docId, _) = SeedDocument();

        var result = await _sut.Handle(
            new BulkReprocessDocumentsCommand([docId]),
            CancellationToken.None);

        Assert.Single(result.JobIds);
    }

    [Fact]
    public async Task Handle_AccessDenied_ThrowsAccessDeniedException()
    {
        var (docId, _) = SeedDocument();

        _queryService.EnsureAllDocumentsWritableAsync(Arg.Any<IQueryable<Guid>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AccessDeniedException());

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => _sut.Handle(
                new BulkReprocessDocumentsCommand([docId]),
                CancellationToken.None));
    }
}
