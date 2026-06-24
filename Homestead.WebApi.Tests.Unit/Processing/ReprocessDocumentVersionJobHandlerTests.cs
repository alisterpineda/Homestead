using System.Text.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class ReprocessDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;
    private readonly IProcessingNotifier _notifier;
    private readonly ISender _sender;
    private readonly ReprocessDocumentVersionJobHandler _handler;

    public ReprocessDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"reprocess-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_mediaPath);

        _textExtractionEngine = Substitute.For<ITextExtractionEngine>();
        _textExtractionEngine.ExtractTextAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _notifier = Substitute.For<IProcessingNotifier>();

        var storage = Options.Create(new FileStorageOptions { MediaPath = _mediaPath });

        var textExtractionHelper = new TextExtractionHelper(_textExtractionEngine);

        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ReprocessVersionTextResult("Test Document"));

        _handler = new ReprocessDocumentVersionJobHandler(
            _db, storage,
            _notifier, textExtractionHelper, _sender);
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
        var payload = new ReprocessDocumentVersionPayload(documentId, versionId);
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
    public async Task HandleAsync_SendsReprocessVersionTextCommand_WithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns("new extracted text");

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ReprocessVersionTextCommand>(c =>
                c.DocumentId == doc.Id &&
                c.VersionId == version.Id &&
                c.ExtractedText == "new extracted text" &&
                c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification_WithResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns("text");

        _sender.Send(Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>())
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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        // Still persists, just no individual notification for the child job.
        await _sender.Received(1).Send(Arg.Any<ReprocessVersionTextCommand>(), ct);
        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(),
            Arg.Any<ProcessingEntityType?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>(),
            Arg.Any<string?>(), Arg.Any<Dictionary<string, string>?>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task HandleAsync_ThrowsWhenVersionNotFound_AndDoesNotSend()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));

        await _sender.DidNotReceive().Send(
            Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_Throws_AndDoesNotSend()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await Assert.ThrowsAsync<TextExtractionFailedException>(
            () => _handler.HandleAsync(job, ct));

        await _sender.DidNotReceive().Send(
            Arg.Any<ReprocessVersionTextCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ErrorImplementsIStructuredJobError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        var ex = await Assert.ThrowsAsync<TextExtractionFailedException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal("text_extraction_failed", ex.ErrorCode);
        Assert.Equal("OCR failed", ex.ErrorData["innerError"]);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedContentType_SendsNullExtractedText()
    {
        var ct = TestContext.Current.CancellationToken;
        // text/plain has no keyed extractor → extraction yields null but succeeds (non-fatal).
        var (doc, version) = SeedDocumentWithVersion("old text", contentType: "text/plain");
        var job = CreateJob(doc.Id, version.Id);

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ReprocessVersionTextCommand>(c => c.ExtractedText == null), ct);
    }
}
