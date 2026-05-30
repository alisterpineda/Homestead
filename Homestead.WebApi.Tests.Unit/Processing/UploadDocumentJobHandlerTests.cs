using Homestead.WebApi.Modules.Documents.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class UploadDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;
    private readonly IProcessingNotifier _notifier;
    private readonly ITextExtractionEngineResolver _engineResolver;
    private readonly IDocumentClassifier _classifier;
    private readonly ThumbnailService _thumbnailService;
    private readonly ISender _sender;
    private readonly UploadDocumentJobHandler _handler;

    public UploadDocumentJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"upload-doc-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot));

        _textExtractionEngine = Substitute.For<ITextExtractionEngine>();
        _textExtractionEngine.ExtractTextAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _notifier = Substitute.For<IProcessingNotifier>();
        _engineResolver = Substitute.For<ITextExtractionEngineResolver>();
        _engineResolver.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new TextExtractionContext(
                TextExtractionEngineType.Tesseract,
                BuiltInTextExtractionEngines.TesseractDefaultConfiguration));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:MediaPath"] = _mediaPath,
                ["FileStorage:CachePath"] = Path.Combine(_mediaPath, "cache")
            })
            .Build();

        var logger = Substitute.For<ILogger<UploadDocumentJobHandler>>();
        _classifier = Substitute.For<IDocumentClassifier>();
        _classifier.IsAutoClassifyEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);
        _thumbnailService = new ThumbnailService(
            Enumerable.Empty<IThumbnailGenerator>(), config,
            Substitute.For<ILogger<ThumbnailService>>());
        var serviceProvider = new ServiceCollection()
            .AddKeyedSingleton(TextExtractionEngineType.Tesseract, _textExtractionEngine)
            .BuildServiceProvider();
        var textExtractionHelper = new TextExtractionHelper(
            serviceProvider, _engineResolver);

        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new CreateDocumentFromUploadResult(Guid.NewGuid(), "Test Document"));

        _handler = new UploadDocumentJobHandler(
            _db, config, logger,
            _notifier, _classifier, _thumbnailService, textExtractionHelper, _sender);
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
        Guid? textExtractionEngineId = null,
        string? name = "Test Document",
        byte[]? content = null)
    {
        var uploadId = Guid.NewGuid();
        var payload = new UploadDocumentPayload(
            uploadId, "test-file.pdf", name, null,
            contentType, 1024, extension, null, textExtractionEngineId);

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
    public async Task HandleAsync_CallsEngineResolverWithPayloadEngineId()
    {
        var ct = TestContext.Current.CancellationToken;
        var engineId = Guid.NewGuid();
        var (job, _) = CreateUploadJob(textExtractionEngineId: engineId);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(engineId, ct);
    }

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithNull_WhenNoEngineIdInPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob(textExtractionEngineId: null);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(null, ct);
    }

    [Fact]
    public async Task HandleAsync_SendsCreateDocumentFromUploadCommand_WithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"send-fields {Guid.NewGuid()}");
        var (job, _) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("extracted text");

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<CreateDocumentFromUploadCommand>(c =>
                c.Name == "Test Document" &&
                c.ExtractedText == "extracted text" &&
                c.ContentType == "application/pdf" &&
                c.FileHash == HashOf(content) &&
                c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification_WithResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        var resultDocumentId = Guid.NewGuid();
        _sender.Send(Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>())
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

        await _sender.Received(1).Send(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.ExtractedText == null), ct);
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.ExtractedText == null), ct);
    }

    [Fact]
    public async Task HandleAsync_MovesFileToFinalLocation_AndCleansUpStaging()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"move-final {Guid.NewGuid()}");
        var (job, stagingPath) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Name == "my-report"), ct);
    }

    [Fact]
    public async Task HandleAsync_CommandFails_DeletesPlacedFinalFile_AndRethrows_StagingCleanedUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"compensation {Guid.NewGuid()}");
        var (job, stagingPath) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _sender.Send(Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateDocumentFromUploadResult>>(_ => throw new Exception("commit failed"));

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(job, ct));

        Assert.False(File.Exists(stagingPath));
        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(HashOf(content), ".pdf"));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_Throws_AndDoesNotSend()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"dupe-content {Guid.NewGuid()}");
        var (job, _) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        var existing = SeedExistingDocumentWithHash(HashOf(content), trashed: false);

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal(existing.Id, ex.ExistingDocumentId);
        Assert.Equal(existing.Name, ex.ExistingDocumentName);
        Assert.Equal("duplicate_content", ex.ErrorCode);
        Assert.False(ex.IsTrashed);
        Assert.Equal("false", ex.ErrorData["isTrashed"]);

        await _sender.DidNotReceive().Send(
            Arg.Any<CreateDocumentFromUploadCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_TrashedDocument_SetsIsTrashed()
    {
        var ct = TestContext.Current.CancellationToken;
        var content = Encoding.UTF8.GetBytes($"dupe-trashed {Guid.NewGuid()}");
        var (job, _) = CreateUploadJob(content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("extracted text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new DocumentClassificationOutput(
                new ClassificationResult([], [], "summary", "AI Title"), [], Guid.NewGuid()));

        await _handler.HandleAsync(job, ct);

        // In-memory overload (name + text), NOT the documentId overload, with the full All scope.
        await _classifier.Received(1).ClassifyAsync(
            "Test Document", "extracted text", Arg.Any<Guid?>(), ClassificationScope.All, ct);

        await _sender.Received(1).Send(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Classification != null && c.ProviderId != null),
            ct);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyDisabled_SkipsClassifier_CommandClassificationNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(false);

        await _handler.HandleAsync(job, ct);

        await _classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());

        await _sender.Received(1).Send(
            Arg.Is<CreateDocumentFromUploadCommand>(c => c.Classification == null && c.ProviderId == null),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ClassificationFails_StillSendsCommandWithNullClassification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<ClassificationScope>(), ct)
            .Returns<DocumentClassificationOutput?>(_ => throw new Exception("AI service unavailable"));

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<ClassificationScope>(), ct)
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
