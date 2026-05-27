using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using System.Security.Cryptography;
using System.Text.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;
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
    private readonly IAuditLogger _auditLogger;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly ITextExtractionEngineResolver _engineResolver;
    private readonly IDocumentClassifier _classifier;
    private readonly ThumbnailService _thumbnailService;
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
        _auditLogger = Substitute.For<IAuditLogger>();
        _auditLogger.ForUser(Arg.Any<Guid?>()).Returns(_auditLogger);
        _searchVectorUpdater = Substitute.For<ISearchVectorUpdater>();
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

        _handler = new UploadDocumentJobHandler(
            _db, config, logger,
            _notifier, _auditLogger, _searchVectorUpdater, _classifier, _thumbnailService, textExtractionHelper);
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
        Guid? textExtractionEngineId = null)
    {
        var uploadId = Guid.NewGuid();
        var payload = new UploadDocumentPayload(
            uploadId, "test-file.pdf", "Test Document", null,
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
        File.WriteAllText(stagingPath, $"fake content {Guid.NewGuid()}");

        return (job, stagingPath);
    }

    [Fact]
    public async Task HandleAsync_CreatesDocumentAndVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("extracted text");

        await _handler.HandleAsync(job, ct);

        var doc = await _db.Documents.FirstOrDefaultAsync(ct);
        Assert.NotNull(doc);
        Assert.Equal("Test Document", doc.Name);

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(ct);
        Assert.NotNull(version);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("extracted text", version.ExtractedText);
        Assert.Equal("application/pdf", version.ContentType);
    }

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
    public async Task HandleAsync_LogsAuditCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.Received(1).ForUser(job.CreatedByUserId);
        _auditLogger.Received(1).LogCreated("Document", Arg.Any<Guid>());
    }

    [Fact]
    public async Task HandleAsync_UpdatesSearchVector()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _searchVectorUpdater.Received(1).UpdateSearchVectorAsync(Arg.Any<Guid>(), ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            Arg.Any<Guid>(),
            "Test Document",
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

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(ct);
        Assert.NotNull(version);
        Assert.Null(version.ExtractedText);
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(ct);
        Assert.NotNull(version);
        Assert.Null(version.ExtractedText);
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job1, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job1, ct);

        // Create a second upload with same file content (same hash)
        var uploadId = Guid.NewGuid();
        var payload1 = job1.GetPayload<UploadDocumentPayload>()!;
        var existingVersion = await _db.DocumentVersions.FirstAsync(ct);

        // Create staging file with same content to get same hash
        var stagingPath2 = Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot, $"{uploadId}.pdf");
        var finalPath = Path.Combine(_mediaPath, existingVersion.FilePath);
        File.Copy(finalPath, stagingPath2);

        var payload2 = new UploadDocumentPayload(
            uploadId, "dupe.pdf", "Duplicate", null,
            "application/pdf", 1024, ".pdf");
        var job2 = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.UploadDocument,
            Status = ProcessingJobStatus.InProgress,
            Payload = JsonSerializer.Serialize(payload2),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job2, ct));

        var existingDoc = await _db.Documents.FirstAsync(ct);
        Assert.Equal(existingDoc.Id, ex.ExistingDocumentId);
        Assert.Equal(existingDoc.Name, ex.ExistingDocumentName);
        Assert.Equal("duplicate_content", ex.ErrorCode);
        Assert.False(ex.IsTrashed);
        Assert.Equal("false", ex.ErrorData["isTrashed"]);
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_TrashedDocument_SetsIsTrashed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job1, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job1, ct);

        // Soft-delete the document
        var doc = await _db.Documents.FirstAsync(ct);
        doc.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Create a second upload with same file content (same hash)
        var uploadId = Guid.NewGuid();
        var existingVersion = await _db.DocumentVersions.IgnoreQueryFilters().FirstAsync(ct);

        var stagingPath2 = Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot, $"{uploadId}.pdf");
        var finalPath = Path.Combine(_mediaPath, existingVersion.FilePath);
        File.Copy(finalPath, stagingPath2);

        var payload2 = new UploadDocumentPayload(
            uploadId, "dupe.pdf", "Duplicate", null,
            "application/pdf", 1024, ".pdf");
        var job2 = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.UploadDocument,
            Status = ProcessingJobStatus.InProgress,
            Payload = JsonSerializer.Serialize(payload2),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job2, ct));

        Assert.Equal(doc.Id, ex.ExistingDocumentId);
        Assert.True(ex.IsTrashed);
        Assert.Equal("true", ex.ErrorData["isTrashed"]);
    }

    [Fact]
    public async Task HandleAsync_MovesFileToFinalLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, stagingPath) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        Assert.False(File.Exists(stagingPath));

        var version = await _db.DocumentVersions.FirstAsync(ct);
        var finalPath = Path.Combine(_mediaPath, version.FilePath);
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

        var doc = await _db.Documents.FirstAsync(ct);
        Assert.Equal("my-report", doc.Name);
    }

    [Fact]
    public async Task HandleAsync_StagingFileCleanedUpOnAnyFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, stagingPath) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _searchVectorUpdater.UpdateSearchVectorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("Search vector update failed")));

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(job, ct));

        Assert.False(File.Exists(stagingPath));
    }

    [Fact]
    public async Task HandleAsync_IdempotentFileMove_WhenFinalPathExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, stagingPath) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        // Compute hash and pre-create the final file to simulate a previous failed attempt
        var content = await File.ReadAllBytesAsync(stagingPath, ct);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(hash, ".pdf"));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Copy(stagingPath, finalPath);

        await _handler.HandleAsync(job, ct);

        Assert.False(File.Exists(stagingPath));
        Assert.True(File.Exists(finalPath));
        var doc = await _db.Documents.FirstOrDefaultAsync(ct);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyEnabled_CallsClassifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct).Returns(true);

        await _handler.HandleAsync(job, ct);

        await _classifier.Received(1).ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifyDisabled_SkipsClassifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(false);

        await _handler.HandleAsync(job, ct);

        await _classifier.DidNotReceive().ClassifyAndApplyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ClassificationFails_StillCompletesUpload()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct)
            .Returns<bool>(x => throw new Exception("AI service unavailable"));

        await _handler.HandleAsync(job, ct);

        var doc = await _db.Documents.FirstOrDefaultAsync(ct);
        Assert.NotNull(doc);

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
    public async Task HandleAsync_NotificationUsesDocumentName_AfterClassification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct)
            .Returns(callInfo =>
            {
                var docId = callInfo.ArgAt<Guid>(0);
                var doc = _db.Documents.Find(docId)!;
                doc.Name = "AI Suggested Name";
                _db.SaveChanges();
                return true;
            });

        await _handler.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            Arg.Any<Guid>(),
            "AI Suggested Name",
            error: null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_AutoClassifySucceeds_SearchVectorNotCalledByHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct).Returns(true);

        await _handler.HandleAsync(job, ct);

        await _searchVectorUpdater.DidNotReceive().UpdateSearchVectorAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ClassificationFails_SearchVectorStillCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct)
            .Returns<bool>(x => throw new Exception("AI service unavailable"));

        await _handler.HandleAsync(job, ct);

        await _searchVectorUpdater.Received(1).UpdateSearchVectorAsync(
            Arg.Any<Guid>(), ct);
    }

    [Fact]
    public async Task HandleAsync_ClassificationCancelled_Propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _classifier.IsAutoClassifyEnabledAsync(ct).Returns(true);
        _classifier.ClassifyAndApplyAsync(Arg.Any<Guid>(), null, ct)
            .Returns<bool>(x => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.HandleAsync(job, ct));
    }
}
