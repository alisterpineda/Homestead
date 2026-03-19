using System.Text.Json;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.TextExtraction;
using Homestead.WebApi.Thumbnails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class UploadDocumentJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractor _textExtractor;
    private readonly IProcessingNotifier _notifier;
    private readonly IAuditLogger _auditLogger;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly ITextExtractionEngineResolver _engineResolver;
    private readonly ThumbnailService _thumbnailService;
    private readonly UploadDocumentJobHandler _handler;

    public UploadDocumentJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"upload-doc-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_mediaPath, DocumentVersion.UploadsSubroot));

        _textExtractor = Substitute.For<ITextExtractor>();
        _textExtractor.CanExtract("application/pdf").Returns(true);

        _notifier = Substitute.For<IProcessingNotifier>();
        _auditLogger = Substitute.For<IAuditLogger>();
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
        _thumbnailService = new ThumbnailService(
            Enumerable.Empty<IThumbnailGenerator>(), config,
            Substitute.For<ILogger<ThumbnailService>>());
        var textExtractionHelper = new TextExtractionHelper(
            new[] { _textExtractor }, _engineResolver);

        _handler = new UploadDocumentJobHandler(
            _db, config, logger,
            _notifier, _auditLogger, _searchVectorUpdater, _thumbnailService, textExtractionHelper);
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

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
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

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(engineId, ct);
    }

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithNull_WhenNoEngineIdInPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob(textExtractionEngineId: null);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(null, ct);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.Received(1).LogCreated("Document", Arg.Any<Guid>(), job.CreatedByUserId);
    }

    [Fact]
    public async Task HandleAsync_UpdatesSearchVector()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _searchVectorUpdater.Received(1).UpdateSearchVectorAsync(Arg.Any<Guid>(), ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
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
    public async Task HandleAsync_NoExtractor_SkipsTextExtraction()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob(contentType: "text/plain", extension: ".txt");

        await _handler.HandleAsync(job, ct);

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(ct);
        Assert.NotNull(version);
        Assert.Null(version.ExtractedText);
        await _engineResolver.DidNotReceive().ResolveAsync(Arg.Any<Guid?>(), ct);
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateUploadJob();

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
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

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
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

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job2, ct));
    }

    [Fact]
    public async Task HandleAsync_MovesFileToFinalLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, stagingPath) = CreateUploadJob();

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
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

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        var doc = await _db.Documents.FirstAsync(ct);
        Assert.Equal("my-report", doc.Name);
    }
}
