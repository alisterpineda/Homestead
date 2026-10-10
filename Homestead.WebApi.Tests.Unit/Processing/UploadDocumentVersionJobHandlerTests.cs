using System.Security.Cryptography;
using System.Text;
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
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class UploadDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;
    private readonly IProcessingNotifier _notifier;
    private readonly ThumbnailService _thumbnailService;
    private readonly IUnitOfWork _unitOfWork;
    private bool _inTransaction;
    private bool _handledInTransaction;
    private readonly IAddDocumentVersionHandler _addDocumentVersion;
    private readonly UploadDocumentVersionJobHandler _handler;

    public UploadDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"upload-ver-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot));

        _textExtractionEngine = TestTextExtraction.Engine();
        _textExtractionEngine.ExtractTextAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output(null));

        _notifier = Substitute.For<IProcessingNotifier>();

        var storage = Options.Create(new FileStorageOptions
        {
            MediaPath = _mediaPath,
            CachePath = Path.Combine(_mediaPath, "cache")
        });

        var logger = Substitute.For<ILogger<UploadDocumentVersionJobHandler>>();
        _thumbnailService = new ThumbnailService(
            Enumerable.Empty<IThumbnailGenerator>(), storage,
            Substitute.For<ILogger<ThumbnailService>>());
        var textExtractionHelper = TestTextExtraction.Helper(_textExtractionEngine);

        _addDocumentVersion = Substitute.For<IAddDocumentVersionHandler>();
        _addDocumentVersion.Handle(Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new AddDocumentVersionResult(Guid.NewGuid(), "Existing Document"));

        // Run the work handed to the transaction, so the command handler is reached through it, and record
        // whether the command handler ran while that work was in progress.
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<AddDocumentVersionResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _inTransaction = true;
                try { return await call.Arg<Func<Task<AddDocumentVersionResult>>>()(); }
                finally { _inTransaction = false; }
            });
        _addDocumentVersion.When(x => x.Handle(Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => _handledInTransaction = _inTransaction);

        _handler = new UploadDocumentVersionJobHandler(
            _db, storage, logger,
            _notifier, _thumbnailService, textExtractionHelper, TestTextExtraction.Resolver(),
            _unitOfWork, _addDocumentVersion);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_mediaPath, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private Document SeedDocument()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Existing Document",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        };
        _db.Documents.Add(doc);
        _db.SaveChanges();
        return doc;
    }

    private (ProcessingJob job, string stagingPath) CreateVersionUploadJob(
        Guid documentId,
        byte[]? content = null)
    {
        var uploadId = Guid.NewGuid();
        var payload = new UploadDocumentVersionPayload(
            uploadId, documentId, "version2.pdf", "v2",
            "application/pdf", 2048, ".pdf");

        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.UploadDocumentVersion,
            Status = ProcessingJobStatus.InProgress,
            Payload = JsonSerializer.Serialize(payload),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var stagingPath = Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot, $"{uploadId}.pdf");
        File.WriteAllBytes(stagingPath, content ?? Encoding.UTF8.GetBytes($"unique version content {Guid.NewGuid()}"));

        return (job, stagingPath);
    }

    private static string HashOf(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    [Fact]
    public async Task HandleAsync_AppliesAddDocumentVersionCommand_WithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"version-fields {Guid.NewGuid()}");
        var (job, _) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("extracted text"));

        await _handler.HandleAsync(job, ct);

        await _addDocumentVersion.Received(1).Handle(
            Arg.Is<AddDocumentVersionCommand>(c =>
                c.DocumentId == doc.Id &&
                c.ExtractedText == "extracted text" &&
                c.FileHash == HashOf(content) &&
                c.Label == "v2"),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ExtractsFirst_ThenSetsActorFromJob_ThenPersistsInsideTheTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        await _handler.HandleAsync(job, ct);

        // Extraction holds no transaction. The actor is set before the transaction opens, so the audit
        // entry is attributed to whoever enqueued the job; the command runs inside the transaction.
        Received.InOrder(() =>
        {
            _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct);
            _unitOfWork.SetActor(job.CreatedByUserId);
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<AddDocumentVersionResult>>>(), ct);
        });
        Assert.True(_handledInTransaction);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification_WithResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        _addDocumentVersion.Handle(Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new AddDocumentVersionResult(Guid.NewGuid(), "Stored Doc Name"));

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
    public async Task HandleAsync_DocumentNotFound_Throws_AndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateVersionUploadJob(Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));

        await _addDocumentVersion.DidNotReceive().Handle(
            Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns<TextExtractionOutput>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        await _addDocumentVersion.Received(1).Handle(
            Arg.Is<AddDocumentVersionCommand>(c => c.ExtractedText == null), ct);
    }

    [Fact]
    public async Task HandleAsync_MovesFileToFinalLocation_AndCleansUpStaging()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"move-final-ver {Guid.NewGuid()}");
        var (job, stagingPath) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        await _handler.HandleAsync(job, ct);

        Assert.False(File.Exists(stagingPath));

        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(HashOf(content), ".pdf"));
        Assert.True(File.Exists(finalPath));
    }

    [Fact]
    public async Task HandleAsync_CommandFails_DeletesPlacedFinalFile_AndRethrows_StagingCleanedUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"compensation-ver {Guid.NewGuid()}");
        var (job, stagingPath) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<AddDocumentVersionResult>>>(), Arg.Any<CancellationToken>())
            .Returns<Task<AddDocumentVersionResult>>(_ => throw new Exception("commit failed"));

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(job, ct));

        Assert.False(File.Exists(stagingPath));
        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(HashOf(content), ".pdf"));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_Throws_AndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"dupe-ver {Guid.NewGuid()}");
        var (job, _) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        SeedExistingVersion(doc, HashOf(content));

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal(doc.Id, ex.ExistingDocumentId);
        Assert.Equal(doc.Name, ex.ExistingDocumentName);
        Assert.Equal("duplicate_content", ex.ErrorCode);
        Assert.False(ex.IsTrashed);
        Assert.Equal("false", ex.ErrorData["isTrashed"]);

        await _addDocumentVersion.DidNotReceive().Handle(
            Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_TrashedDocument_SetsIsTrashed()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"dupe-ver-trashed {Guid.NewGuid()}");

        // The matching content lives on a (soft-deleted) document; the upload targets a different doc.
        var trashedDoc = SeedDocument();
        SeedExistingVersion(trashedDoc, HashOf(content));
        trashedDoc.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var targetDoc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(targetDoc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal(trashedDoc.Id, ex.ExistingDocumentId);
        Assert.True(ex.IsTrashed);
        Assert.Equal("true", ex.ErrorData["isTrashed"]);
    }

    private void SeedExistingVersion(Document doc, string fileHash)
    {
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = 1,
            ContentType = "application/pdf",
            FileSize = 1024,
            FilePath = DocumentVersion.GetFilePath(fileHash, ".pdf"),
            FileHash = fileHash,
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = doc.Id
        });
        _db.SaveChanges();
    }
}
