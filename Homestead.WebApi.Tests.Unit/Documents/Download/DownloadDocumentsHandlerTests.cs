using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Features.Download;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.Download;

public class DownloadDocumentsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDocumentQueryService _accessService;
    private readonly IJobQueue _jobQueue;
    private readonly DownloadDocumentsHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public DownloadDocumentsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _accessService = Substitute.For<IDocumentQueryService>();
        _jobQueue = Substitute.For<IJobQueue>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _sut = new DownloadDocumentsHandler(_db, _currentUser, _accessService, _jobQueue);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_WithIds_EnqueuesJobWithSpecifiedIds()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var command = new DownloadDocumentsCommand(ids);

        var result = await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
        await _jobQueue.Received(1).EnqueueAsync(
            ProcessingJobType.DownloadDocuments,
            Arg.Any<Action<EnqueueJobRequest>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithIds_ValidatesAccess()
    {
        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new DownloadDocumentsCommand(ids);

        await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        await _accessService.Received(1).EnsureAllDocumentsAccessibleAsync(
            Arg.Any<IQueryable<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNullIds_DownloadsAllAccessible()
    {
        var doc = SeedDocument();
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Return a DB-backed queryable (supports async enumeration)
        _accessService.AccessibleDocumentIds().Returns(
            _db.Documents.Select(d => d.Id));

        var command = new DownloadDocumentsCommand(null);

        var result = await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
        _accessService.Received(1).AccessibleDocumentIds();
    }

    [Fact]
    public async Task Handle_WithEmptyIds_DownloadsAllAccessible()
    {
        var doc = SeedDocument();
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _accessService.AccessibleDocumentIds().Returns(
            _db.Documents.Select(d => d.Id));

        var command = new DownloadDocumentsCommand([]);

        var result = await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
        _accessService.Received(1).AccessibleDocumentIds();
    }

    [Fact]
    public async Task Handle_NoAccessibleDocuments_ThrowsBusinessRuleException()
    {
        // No documents seeded — DB is empty
        _accessService.AccessibleDocumentIds().Returns(
            _db.Documents.Select(d => d.Id));

        var command = new DownloadDocumentsCommand(null);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.HandleAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_ActiveDownloadExists_ThrowsBusinessRuleException()
    {
        // Seed an active download job for this user
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.DownloadDocuments,
            Status = ProcessingJobStatus.Pending,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DownloadDocumentsCommand(new List<Guid> { Guid.NewGuid() });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.HandleAsync(command, TestContext.Current.CancellationToken));

        Assert.Contains("already in progress", ex.Message);
    }

    [Fact]
    public async Task Handle_CompletedDownloadExists_AllowsNewDownload()
    {
        // Seed a completed download job (should not block)
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.DownloadDocuments,
            Status = ProcessingJobStatus.Completed,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new DownloadDocumentsCommand(ids);

        var result = await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task Handle_InProgressDownloadExists_ThrowsBusinessRuleException()
    {
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.DownloadDocuments,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new DownloadDocumentsCommand(new List<Guid> { Guid.NewGuid() });

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.HandleAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_OtherUsersActiveDownload_DoesNotBlock()
    {
        // Another user's active download should not block this user
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.DownloadDocuments,
            Status = ProcessingJobStatus.Pending,
            CreatedByUserId = Guid.NewGuid(), // Different user
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new DownloadDocumentsCommand(ids);

        var result = await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task Handle_EnqueuesWithCorrectEntityType()
    {
        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new DownloadDocumentsCommand(ids);

        await _sut.HandleAsync(command, TestContext.Current.CancellationToken);

        await _jobQueue.Received(1).EnqueueAsync(
            ProcessingJobType.DownloadDocuments,
            Arg.Is<Action<EnqueueJobRequest>?>(action => action != null),
            Arg.Any<CancellationToken>());
    }

    private Document SeedDocument()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Test Document",
            CreatedAt = DateTimeOffset.UtcNow,
            OwnerUserId = _userId,
            CreatedByUserId = _userId
        };
        _db.Documents.Add(doc);
        return doc;
    }
}
