using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class ReprocessDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;

    // A second configured engine, so the timeout tests can tell "the stamped engine" from "the built-in one".
    private readonly ITextExtractionEngine _doclingEngine = TestTextExtraction.Engine("docling");
    private readonly IProcessingNotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private bool _inTransaction;
    private bool _handledInTransaction;
    private readonly IReprocessVersionTextHandler _reprocessVersionText;
    private readonly ReprocessDocumentVersionJobHandler _handler;

    public ReprocessDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"reprocess-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_mediaPath);

        _textExtractionEngine = TestTextExtraction.Engine();
        _textExtractionEngine.ExtractTextAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output(null));

        _notifier = Substitute.For<IProcessingNotifier>();

        var storage = Options.Create(new FileStorageOptions { MediaPath = _mediaPath });

        var textExtractionHelper = TestTextExtraction.Helper(_textExtractionEngine, _doclingEngine);

        _reprocessVersionText = Substitute.For<IReprocessVersionTextHandler>();
        _reprocessVersionText.HandleAsync(Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ReprocessVersionTextResult("Test Document"));

        // Run the work handed to the transaction, so the command handler is reached through it, and record
        // whether the command handler ran while that work was in progress.
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<ReprocessVersionTextResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _inTransaction = true;
                try { return await call.Arg<Func<Task<ReprocessVersionTextResult>>>()(); }
                finally { _inTransaction = false; }
            });
        _reprocessVersionText.When(x => x.HandleAsync(Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => _handledInTransaction = _inTransaction);

        _handler = new ReprocessDocumentVersionJobHandler(
            _db, storage,
            _notifier, textExtractionHelper, _unitOfWork, _reprocessVersionText);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_mediaPath, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (Document doc, DocumentVersion version) SeedDocumentWithVersion(
        string? extractedText = "old text",
        string contentType = "application/pdf")
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Test Document",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _db.Documents.Add(doc);

        var fileHash = Guid.NewGuid().ToString("N");
        var filePath = DocumentVersion.GetFilePath(fileHash, ".pdf");

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = 1,
            ContentType = contentType,
            FileSize = 1024,
            FilePath = filePath,
            FileHash = fileHash,
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = doc.Id,
            ExtractedText = extractedText
        };
        _db.DocumentVersions.Add(version);
        _db.SaveChanges();

        // Create the physical file so the handler's File.Exists check passes.
        var physicalPath = Path.Combine(_mediaPath, filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        File.WriteAllText(physicalPath, "fake pdf content");

        return (doc, version);
    }

    private ProcessingJob CreateJob(Guid documentId, Guid versionId)
    {
        var payload = new ReprocessDocumentVersionPayload(
            documentId, versionId, TestTextExtraction.DefaultKey);
        return new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ReprocessDocumentVersion,
            Status = ProcessingJobStatus.InProgress,
            Payload = JsonSerializer.Serialize(payload),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task HandleAsync_AppliesReprocessVersionTextCommand_WithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output("new extracted text"));

        await _handler.HandleAsync(job, ct);

        await _reprocessVersionText.Received(1).HandleAsync(
            Arg.Is<ReprocessVersionTextCommand>(c =>
                c.DocumentId == doc.Id &&
                c.VersionId == version.Id &&
                c.ExtractedText == "new extracted text"),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ExtractsFirst_ThenSetsActorFromJob_ThenPersistsInsideTheTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output("text"));

        await _handler.HandleAsync(job, ct);

        // Extraction holds no transaction. The actor is set before the transaction opens, so the audit
        // entry is attributed to whoever enqueued the job; the command runs inside the transaction.
        Received.InOrder(() =>
        {
            _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _unitOfWork.SetActor(job.CreatedByUserId);
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<ReprocessVersionTextResult>>>(), ct);
        });
        Assert.True(_handledInTransaction);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification_WithResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output("text"));

        _reprocessVersionText.HandleAsync(Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ReprocessVersionTextResult("Stored Doc Name"));

        await _handler.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            doc.Id,
            "Stored Doc Name",
            error: null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_SkipsNotification_WhenChildJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);
        job.ParentJobId = Guid.NewGuid();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output("text"));

        await _handler.HandleAsync(job, ct);

        // Still persists, just no individual notification for the child job.
        await _reprocessVersionText.Received(1).HandleAsync(Arg.Any<ReprocessVersionTextCommand>(), ct);
        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(),
            Arg.Any<ProcessingEntityType?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>(),
            Arg.Any<string?>(), Arg.Any<Dictionary<string, string>?>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task HandleAsync_ThrowsWhenVersionNotFound_AndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));

        await _reprocessVersionText.DidNotReceive().HandleAsync(
            Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_Throws_AndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TextExtractionOutput>(x => throw new Exception("OCR failed"));

        await Assert.ThrowsAsync<TextExtractionFailedException>(
            () => _handler.HandleAsync(job, ct));

        await _reprocessVersionText.DidNotReceive().HandleAsync(
            Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ErrorImplementsIStructuredJobError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TextExtractionOutput>(x => throw new Exception("OCR failed"));

        var ex = await Assert.ThrowsAsync<TextExtractionFailedException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal("text_extraction_failed", ex.ErrorCode);
        Assert.Equal("OCR failed", ex.ErrorData["innerError"]);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedContentType_PersistsNullExtractedText()
    {
        var ct = TestContext.Current.CancellationToken;
        // text/plain has no keyed extractor → extraction yields null but succeeds (non-fatal).
        var (doc, version) = SeedDocumentWithVersion("old text", contentType: "text/plain");
        var job = CreateJob(doc.Id, version.Id);

        await _handler.HandleAsync(job, ct);

        await _reprocessVersionText.Received(1).HandleAsync(
            Arg.Is<ReprocessVersionTextCommand>(c => c.ExtractedText == null), ct);
    }

    // --- Per-engine job budget ---

    [Fact]
    public void ResolveTimeout_IsTheStampedEnginesJobTimeout()
    {
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());
        job.Payload = JsonSerializer.Serialize(
            new ReprocessDocumentVersionPayload(Guid.NewGuid(), Guid.NewGuid(), "docling"));
        _textExtractionEngine.JobTimeout.Returns(TimeSpan.FromMinutes(1));
        _doclingEngine.JobTimeout.Returns(TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(20), _handler.ResolveTimeout(job));
    }

    [Fact]
    public void ResolveTimeout_EngineWithoutABudget_IsNull()
    {
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(_handler.ResolveTimeout(job));
    }

    // A key that no longer resolves runs the built-in engine, so it gets that engine's budget.
    [Fact]
    public void ResolveTimeout_DanglingStampedKey_IsTheBuiltInEnginesBudget()
    {
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());
        job.Payload = JsonSerializer.Serialize(
            new ReprocessDocumentVersionPayload(Guid.NewGuid(), Guid.NewGuid(), "deleted-engine"));
        _textExtractionEngine.JobTimeout.Returns(TimeSpan.FromMinutes(7));

        Assert.Equal(TimeSpan.FromMinutes(7), _handler.ResolveTimeout(job));
    }

    [Fact]
    public void ResolveTimeout_NoPayload_IsNull()
    {
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());
        job.Payload = null;

        Assert.Null(_handler.ResolveTimeout(job));
    }

    // A reprocess has no "carry on without text": past its deadline it fails loudly, as a structured
    // text_extraction_failed rather than a job timeout.
    [Fact]
    public async Task HandleAsync_ExtractionPastItsDeadline_FailsAsTextExtractionFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);
        _textExtractionEngine.JobTimeout.Returns(TimeSpan.FromMilliseconds(200));
        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return TestTextExtraction.Output("never");
            });

        var ex = await Assert.ThrowsAsync<TextExtractionFailedException>(() => _handler.HandleAsync(job, ct));

        Assert.IsType<TimeoutException>(ex.InnerException);
        await _reprocessVersionText.DidNotReceive().HandleAsync(
            Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>());
    }
}
