using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ReprocessDocumentVersionHandlerTests : IDisposable
{
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

        _sut = new ReprocessDocumentVersionHandler(_db, _currentUser, _jobQueue);

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
}
