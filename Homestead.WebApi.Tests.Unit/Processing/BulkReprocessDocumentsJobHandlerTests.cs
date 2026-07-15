using System.Text.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class BulkReprocessDocumentsJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IJobSignal _signal;
    private readonly BulkReprocessDocumentsJobHandler _handler;

    public BulkReprocessDocumentsJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _signal = Substitute.For<IJobSignal>();

        _handler = new BulkReprocessDocumentsJobHandler(_db, _signal);
    }

    public void Dispose() => _db.Dispose();

    private Guid SeedDocument(string name = "Test.pdf", int versionCount = 1)
    {
        var docId = Guid.NewGuid();
        _db.Documents.Add(new Document
        {
            Id = docId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        for (var i = 1; i <= versionCount; i++)
        {
            _db.DocumentVersions.Add(new DocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = docId,
                VersionNumber = i,
                ContentType = "application/pdf",
                FileSize = 1024,
                FilePath = $"documents/files/{Guid.NewGuid()}.pdf",
                FileHash = $"hash-{Guid.NewGuid()}",
                FileExtension = ".pdf",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _db.SaveChanges();
        return docId;
    }

    private ProcessingJob CreateParentJob(List<Guid> documentIds)
    {
        var payload = new BulkReprocessPayload(documentIds, TestTextExtraction.DefaultKey);
        return new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.BulkReprocessDocuments,
            Status = ProcessingJobStatus.InProgress,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        };
    }

    [Fact]
    public async Task HandleAsync_CreatesChildJobPerDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId1 = SeedDocument("Doc1.pdf");
        var docId2 = SeedDocument("Doc2.pdf");
        var job = CreateParentJob([docId1, docId2]);

        await _handler.HandleAsync(job, ct);

        var children = await _db.ProcessingJobs.Where(j => j.ParentJobId == job.Id).ToListAsync(ct);
        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.Equal(ProcessingJobType.ReprocessDocumentVersion, c.Type));
        Assert.All(children, c => Assert.Equal(ProcessingJobStatus.Pending, c.Status));
        Assert.Equal(2, job.TotalChildCount);
    }

    [Fact]
    public async Task HandleAsync_SetsParentJobIdOnChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId = SeedDocument();
        var job = CreateParentJob([docId]);

        await _handler.HandleAsync(job, ct);

        var child = await _db.ProcessingJobs.SingleAsync(j => j.ParentJobId == job.Id, ct);
        Assert.Equal(job.Id, child.ParentJobId);
    }

    [Fact]
    public async Task HandleAsync_SelectsLatestVersionPerDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId = SeedDocument("Multi.pdf", versionCount: 3);
        var job = CreateParentJob([docId]);

        var latestVersion = _db.DocumentVersions
            .Where(v => v.DocumentId == docId)
            .OrderByDescending(v => v.VersionNumber)
            .First();

        await _handler.HandleAsync(job, ct);

        var child = await _db.ProcessingJobs.SingleAsync(j => j.ParentJobId == job.Id, ct);
        Assert.Contains(latestVersion.Id.ToString(), child.Payload!);
    }

    // The fan-out copies the parent's already-resolved key rather than re-resolving per child. Without
    // this, flipping the household default mid-run would split one batch across two engines, with no
    // record of which document got which.
    [Fact]
    public async Task HandleAsync_CopiesTheParentEngineKeyIntoEveryChild()
    {
        var ct = TestContext.Current.CancellationToken;
        var docIds = new List<Guid> { SeedDocument("A.pdf"), SeedDocument("B.pdf") };
        var job = CreateParentJob(docIds);

        await _handler.HandleAsync(job, ct);

        var children = await _db.ProcessingJobs.Where(j => j.ParentJobId == job.Id).ToListAsync(ct);
        Assert.Equal(2, children.Count);
        Assert.All(children, child =>
        {
            var payload = JsonSerializer.Deserialize<ReprocessDocumentVersionPayload>(child.Payload!);
            Assert.Equal(TestTextExtraction.DefaultKey, payload!.EngineKey);
        });
    }

    [Fact]
    public async Task HandleAsync_IdempotentOnRerun_DoesNotCreateDuplicateChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId = SeedDocument();
        var job = CreateParentJob([docId]);

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ReprocessDocumentVersion,
            Status = ProcessingJobStatus.Pending,
            ParentJobId = job.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        await _handler.HandleAsync(job, ct);

        var children = await _db.ProcessingJobs.Where(j => j.ParentJobId == job.Id).ToListAsync(ct);
        Assert.Single(children);
    }

    [Fact]
    public async Task HandleAsync_IdempotentOnRerun_RestoresTotalChildCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateParentJob([Guid.NewGuid()]);

        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ReprocessDocumentVersion,
            Status = ProcessingJobStatus.Completed,
            ParentJobId = job.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.ProcessingJobs.Add(new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ReprocessDocumentVersion,
            Status = ProcessingJobStatus.Pending,
            ParentJobId = job.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        await _handler.HandleAsync(job, ct);

        Assert.Equal(2, job.TotalChildCount);
    }

    [Fact]
    public async Task HandleAsync_NoMatchingDocuments_SetsTotalChildCountToZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateParentJob([Guid.NewGuid()]);

        await _handler.HandleAsync(job, ct);

        Assert.Equal(0, job.TotalChildCount);
        var children = await _db.ProcessingJobs.Where(j => j.ParentJobId == job.Id).ToListAsync(ct);
        Assert.Empty(children);
    }

    [Fact]
    public async Task HandleAsync_PassesCreatedByUserIdToChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId = SeedDocument();
        var job = CreateParentJob([docId]);

        await _handler.HandleAsync(job, ct);

        var child = await _db.ProcessingJobs.SingleAsync(j => j.ParentJobId == job.Id, ct);
        Assert.Equal(job.CreatedByUserId, child.CreatedByUserId);
    }

    [Fact]
    public async Task HandleAsync_SignalsOnceAfterBatchInsert()
    {
        var ct = TestContext.Current.CancellationToken;
        var docId1 = SeedDocument("Doc1.pdf");
        var docId2 = SeedDocument("Doc2.pdf");
        var job = CreateParentJob([docId1, docId2]);

        await _handler.HandleAsync(job, ct);

        _signal.Received(1).Signal();
    }
}
