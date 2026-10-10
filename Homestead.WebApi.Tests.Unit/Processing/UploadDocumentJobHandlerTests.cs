using Homestead.WebApi.Modules.Documents.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class UploadDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;
    private readonly IProcessingNotifier _notifier;
    private readonly IDocumentClassifier _classifier;
    private readonly IAiProviderResolver _aiProviderResolver;
    private readonly AiPageRenderService _aiPageRenderService;
    private readonly ThumbnailService _thumbnailService;
    private readonly IUnitOfWork _unitOfWork;
    private bool _inTransaction;
    private bool _handledInTransaction;
    private readonly ICreateDocumentFromUploadHandler _createDocument;
    private readonly UploadDocumentJobHandler _handler;

    public UploadDocumentJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"upload-doc-test-{Guid.NewGuid()}");
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

        var logger = Substitute.For<ILogger<UploadDocumentJobHandler>>();
        _classifier = Substitute.For<IDocumentClassifier>();
        _classifier.IsAutoClassifyEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);
        _aiProviderResolver = TestAiProviders.Resolver(TestAiProviders.Provider());
        // GetOrCreateAsync is virtual; substitute returns null by default (no image) unless a test stubs it.
        _aiPageRenderService = Substitute.For<AiPageRenderService>(
            Substitute.For<IDocumentPageRenderer>(),
            storage,
            Substitute.For<ILogger<AiPageRenderService>>());
        _thumbnailService = new ThumbnailService(
            Enumerable.Empty<IThumbnailGenerator>(), storage,
            Substitute.For<ILogger<ThumbnailService>>());
        var textExtractionHelper = TestTextExtraction.Helper(_textExtractionEngine);

        _createDocument = Substitute.For<ICreateDocumentFromUploadHandler>();
        _createDocument.Handle(Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new CreateDocumentFromUploadResult(Guid.NewGuid(), "Test Document"));

        // Run the work handed to the transaction, so the command handler is reached through it, and record
        // whether the command handler ran while that work was in progress.
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<CreateDocumentFromUploadResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _inTransaction = true;
                try { return await call.Arg<Func<Task<CreateDocumentFromUploadResult>>>()(); }
                finally { _inTransaction = false; }
            });
        _createDocument.When(x => x.Handle(Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => _handledInTransaction = _inTransaction);

        _handler = new UploadDocumentJobHandler(
            _db, storage, logger,
            _notifier, _classifier, _aiProviderResolver, _aiPageRenderService, _thumbnailService,
            textExtractionHelper, TestTextExtraction.Resolver(), _unitOfWork, _createDocument);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_mediaPath, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (ProcessingJob job, string stagingPath) CreateUploadJob(
        string contentType = "application/pdf",
        string extension = ".pdf",
        string? name = "Test Document",
        byte[]? content = null)
    {
        var uploadId = Guid.NewGuid();
        var payload = new UploadDocumentPayload(
            uploadId, "test-file.pdf", name, null,
            contentType, 1024, extension, null);

        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.UploadDocument,
            Status = ProcessingJobStatus.InProgress,
            Payload = JsonSerializer.Serialize(payload),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var stagingPath = Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot, $"{uploadId}{extension}");
        File.WriteAllBytes(stagingPath, content ?? Encoding.UTF8.GetBytes($"fake content {Guid.NewGuid()}"));

        return (job, stagingPath);
    }

    private static string HashOf(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    [Fact]
    public async Task HandleAsync_AppliesCreateDocumentFromUploadCommand_WithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"send-fields {Guid.NewGuid()}");
        var (job, _) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("extracted text"));

        await _handler.HandleAsync(job, ct);

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c =>
                c.Name == "Test Document" &&
                c.ExtractedText == "extracted text" &&
                c.ContentType == "application/pdf" &&
                c.FileHash == HashOf(content) &&
                c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ClassifiesFirst_ThenSetsActorFromJob_ThenCreatesInsideTheTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);

        await _handler.HandleAsync(job, ct);

        // Extraction and the slow AI call hold no transaction. The actor is set before the transaction
        // opens, so the Created audit entry is attributed to the uploader; the command runs inside it.
        Received.InOrder(() =>
        {
            _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct);
            _classifier.ClassifyAsync(
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), ClassificationScope.All, ct);
            _unitOfWork.SetActor(job.CreatedByUserId);
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<CreateDocumentFromUploadResult>>>(), ct);
        });
        Assert.True(_handledInTransaction);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification_WithResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        var resultDocumentId = Guid.NewGuid();
        _createDocument.Handle(Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new CreateDocumentFromUploadResult(resultDocumentId, "Stored Name"));

        await _handler.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            resultDocumentId,
            "Stored Name",
            error: null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedContentType_SkipsTextExtraction()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob(contentType: "text/plain", extension: ".txt");

        await _handler.HandleAsync(job, ct);

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.ExtractedText == null), ct);
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns<TextExtractionOutput>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.ExtractedText == null), ct);
    }

    [Fact]
    public async Task HandleAsync_MovesFileToFinalLocation_AndCleansUpStaging()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"move-final {Guid.NewGuid()}");
        var (job, stagingPath) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        await _handler.HandleAsync(job, ct);

        Assert.False(File.Exists(stagingPath));

        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(HashOf(content), ".pdf"));
        Assert.True(File.Exists(finalPath));
    }

    [Fact]
    public async Task HandleAsync_UsesOriginalFileNameWhenNoNameProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        var uploadId = Guid.NewGuid();
        var payload = new UploadDocumentPayload(
            uploadId, "my-report.pdf", null, null,
            "application/pdf", 1024, ".pdf");

        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.UploadDocument,
            Status = ProcessingJobStatus.InProgress,
            Payload = JsonSerializer.Serialize(payload),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var stagingPath = Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot, $"{uploadId}.pdf");
        File.WriteAllText(stagingPath, $"unique content {Guid.NewGuid()}");

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        await _handler.HandleAsync(job, ct);

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Name == "my-report"), ct);
    }

    [Fact]
    public async Task HandleAsync_CommandFails_DeletesPlacedFinalFile_AndRethrows_StagingCleanedUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"compensation {Guid.NewGuid()}");
        var (job, stagingPath) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<CreateDocumentFromUploadResult>>>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateDocumentFromUploadResult>>(_ => throw new Exception("commit failed"));

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(job, ct));

        Assert.False(File.Exists(stagingPath));
        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(HashOf(content), ".pdf"));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_Throws_AndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"dupe-content {Guid.NewGuid()}");
        var (job, _) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        var existing = SeedExistingDocumentWithHash(HashOf(content), trashed: false);

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal(existing.Id, ex.ExistingDocumentId);
        Assert.Equal(existing.Name, ex.ExistingDocumentName);
        Assert.Equal("duplicate_content", ex.ErrorCode);
        Assert.False(ex.IsTrashed);
        Assert.Equal("false", ex.ErrorData["isTrashed"]);

        await _createDocument.DidNotReceive().Handle(
            Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_TrashedDocument_SetsIsTrashed()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"dupe-trashed {Guid.NewGuid()}");
        var (job, _) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        var existing = SeedExistingDocumentWithHash(HashOf(content), trashed: true);

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal(existing.Id, ex.ExistingDocumentId);
        Assert.True(ex.IsTrashed);
        Assert.Equal("true", ex.ErrorData["isTrashed"]);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyEnabled_CallsInMemoryClassifier_AndFoldsIntoCommand()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("extracted text"));

        var renderBytes = new byte[] { 1, 2, 3 };
        _aiPageRenderService.GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct)
            .Returns(renderBytes);

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new DocumentClassificationOutput(
                new ClassificationResult([], [], "summary", "AI Title"), []));

        await _handler.HandleAsync(job, ct);

        // In-memory overload (name + text + rendered page image), NOT the documentId overload, with the
        // full All scope — and the bytes the render service produced are forwarded to the classifier.
        await _classifier.Received(1).ClassifyAsync(
            "Test Document", "extracted text", renderBytes, ClassificationScope.All, ct);

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Classification != null),
            ct);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyEnabled_AiUnresolved_SkipsRender()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("extracted text"));

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _aiProviderResolver.Resolve(Arg.Any<string?>()).Returns((IAiProvider?)null);

        await _handler.HandleAsync(job, ct);

        // AI unconfigured -> classify no-ops anyway, so don't waste a render + cache write.
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        // The classifier is still invoked (it short-circuits internally) with a null page image.
        await _classifier.Received(1).ClassifyAsync(
            "Test Document", "extracted text", null, ClassificationScope.All, ct);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyEnabled_ImageInputOff_SkipsRender()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("extracted text"));

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        // Built before .Returns — configuring one substitute inside another's Returns trips NSubstitute.
        var textOnlyProvider = TestAiProviders.Provider(options: new AiProviderOptions
        {
            Type = AiProviderType.OpenAiCompatible,
            Endpoint = "http://x/v1",
            Model = "m",
            ImageInput = false,
        });
        _aiProviderResolver.Resolve(Arg.Any<string?>()).Returns(textOnlyProvider);

        await _handler.HandleAsync(job, ct);

        // Provider resolves but has the image kill-switch off -> classify runs text-only, no render.
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _classifier.Received(1).ClassifyAsync(
            "Test Document", "extracted text", null, ClassificationScope.All, ct);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyDisabled_SkipsClassifier_CommandClassificationNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(false);

        await _handler.HandleAsync(job, ct);

        await _classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<byte[]?>(),
            Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Classification == null),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ClassificationFails_StillCreatesWithNullClassification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<ClassificationScope>(), ct)
            .Returns<DocumentClassificationOutput?>(_ => throw new Exception("AI service unavailable"));

        await _handler.HandleAsync(job, ct);

        await _createDocument.Received(1).Handle(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Classification == null), ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            Arg.Any<ProcessingEntityType?>(),
            Arg.Any<Guid?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<Guid?>(),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ClassificationCancelled_Propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), ct)
            .Returns(TestTextExtraction.Output("text"));

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<ClassificationScope>(), ct)
            .Returns<DocumentClassificationOutput?>(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.HandleAsync(job, ct));
    }

    private Document SeedExistingDocumentWithHash(string fileHash, bool trashed)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Name = "Existing Document",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid(),
            DeletedAt = trashed ? DateTimeOffset.UtcNow : null
        };
        _db.Documents.Add(doc);
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
        return doc;
    }
}
