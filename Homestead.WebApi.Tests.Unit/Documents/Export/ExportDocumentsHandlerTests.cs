using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Features.Documents.Export;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.Export;

public class ExportDocumentsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDocumentQueryService _accessService;
    private readonly IJobQueue _jobQueue;
    private readonly ExportDocumentsHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public ExportDocumentsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _accessService = Substitute.For<IDocumentQueryService>();
        _jobQueue = Substitute.For<IJobQueue>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _sut = new ExportDocumentsHandler(_db, _currentUser, _accessService, _jobQueue);
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
        var command = new ExportDocumentsCommand(ids);

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
        await _jobQueue.Received(1).EnqueueAsync(
            ProcessingJobType.ExportDocuments,
            Arg.Any<Action<EnqueueJobRequest>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithIds_ValidatesAccess()
    {
        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new ExportDocumentsCommand(ids);

        await _sut.Handle(command, CancellationToken.None);

        await _accessService.Received(1).EnsureAllDocumentsAccessibleAsync(
            Arg.Any<IQueryable<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNullIds_ExportsAllAccessible()
    {
        var doc = SeedDocument();
        await _db.SaveChangesAsync();
        // Return a DB-backed queryable (supports async enumeration)
        _accessService.AccessibleDocumentIds().Returns(
            _db.Documents.Select(d => d.Id));

        var command = new ExportDocumentsCommand(null);

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
        _accessService.Received(1).AccessibleDocumentIds();
    }

    [Fact]
    public async Task Handle_WithEmptyIds_ExportsAllAccessible()
    {
        var doc = SeedDocument();
        await _db.SaveChangesAsync();
        _accessService.AccessibleDocumentIds().Returns(
            _db.Documents.Select(d => d.Id));

        var command = new ExportDocumentsCommand([]);

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
        _accessService.Received(1).AccessibleDocumentIds();
    }

    [Fact]
    public async Task Handle_NoAccessibleDocuments_ThrowsBusinessRuleException()
    {
        // No documents seeded — DB is empty
        _accessService.AccessibleDocumentIds().Returns(
            _db.Documents.Select(d => d.Id));

        var command = new ExportDocumentsCommand(null);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ActiveExportExists_ThrowsBusinessRuleException()
    {
        // Seed an active export job for this user
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ExportDocuments,
            Status = ProcessingJobStatus.Pending,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var command = new ExportDocumentsCommand(new List<Guid> { Guid.NewGuid() });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(command, CancellationToken.None));

        Assert.Contains("already in progress", ex.Message);
    }

    [Fact]
    public async Task Handle_CompletedExportExists_AllowsNewExport()
    {
        // Seed a completed export job (should not block)
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ExportDocuments,
            Status = ProcessingJobStatus.Completed,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new ExportDocumentsCommand(ids);

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task Handle_InProgressExportExists_ThrowsBusinessRuleException()
    {
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ExportDocuments,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var command = new ExportDocumentsCommand(new List<Guid> { Guid.NewGuid() });

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OtherUsersActiveExport_DoesNotBlock()
    {
        // Another user's active export should not block this user
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ExportDocuments,
            Status = ProcessingJobStatus.Pending,
            CreatedByUserId = Guid.NewGuid(), // Different user
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new ExportDocumentsCommand(ids);

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task Handle_EnqueuesWithCorrectEntityType()
    {
        var ids = new List<Guid> { Guid.NewGuid() };
        var command = new ExportDocumentsCommand(ids);

        await _sut.Handle(command, CancellationToken.None);

        await _jobQueue.Received(1).EnqueueAsync(
            ProcessingJobType.ExportDocuments,
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
