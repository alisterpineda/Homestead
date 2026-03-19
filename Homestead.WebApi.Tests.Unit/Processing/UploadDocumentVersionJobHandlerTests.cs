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

public class UploadDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractor _textExtractor;
    private readonly IProcessingNotifier _notifier;
    private readonly IAuditLogger _auditLogger;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly ITextExtractionEngineResolver _engineResolver;
    private readonly ThumbnailService _thumbnailService;
    private readonly UploadDocumentVersionJobHandler _handler;

    public UploadDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"upload-ver-test-{Guid.NewGuid()}");
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

        var logger = Substitute.For<ILogger<UploadDocumentVersionJobHandler>>();
        _thumbnailService = new ThumbnailService(
            Enumerable.Empty<IThumbnailGenerator>(), config,
            Substitute.For<ILogger<ThumbnailService>>());
        var textExtractionHelper = new TextExtractionHelper(
            new[] { _textExtractor }, _engineResolver);

        _handler = new UploadDocumentVersionJobHandler(
            _db, config, logger,
            _notifier, _auditLogger, _searchVectorUpdater, _thumbnailService, textExtractionHelper);
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
        Guid? textExtractionEngineId = null)
    {
        var uploadId = Guid.NewGuid();
        var payload = new UploadDocumentVersionPayload(
            uploadId, documentId, "version2.pdf", "v2",
            "application/pdf", 2048, ".pdf", textExtractionEngineId);

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
        File.WriteAllText(stagingPath, $"unique version content {Guid.NewGuid()}");

        return (job, stagingPath);
    }

    [Fact]
    public async Task HandleAsync_CreatesVersionWithCorrectNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("version 2 text");

        await _handler.HandleAsync(job, ct);

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(ct);
        Assert.NotNull(version);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("v2", version.Label);
        Assert.Equal("version 2 text", version.ExtractedText);
        Assert.Equal(doc.Id, version.DocumentId);
    }

    [Fact]
    public async Task HandleAsync_IncrementsVersionNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();

        // Add existing version 1
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = 1,
            ContentType = "application/pdf",
            FileSize = 1024,
            FilePath = "files/ab/cd/old.pdf",
            FileHash = Guid.NewGuid().ToString("N"),
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = doc.Id
        });
        await _db.SaveChangesAsync(ct);

        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        var newVersion = await _db.DocumentVersions
            .Where(v => v.VersionNumber == 2)
            .FirstOrDefaultAsync(ct);
        Assert.NotNull(newVersion);
        Assert.Equal(2, newVersion.VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithPayloadEngineId()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var engineId = Guid.NewGuid();
        var (job, _) = CreateVersionUploadJob(doc.Id, textExtractionEngineId: engineId);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(engineId, ct);
    }

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithNull_WhenNoEngineIdInPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id, textExtractionEngineId: null);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(null, ct);
    }

    [Fact]
    public async Task HandleAsync_LogsAuditCreatedForVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.Received(1).LogCreated(
            "DocumentVersion", Arg.Any<Guid>(), job.CreatedByUserId, parentEntityId: doc.Id);
    }

    [Fact]
    public async Task HandleAsync_UpdatesSearchVector()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _searchVectorUpdater.Received(1).UpdateSearchVectorAsync(doc.Id, ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            doc.Id,
            "Existing Document",
            error: null,
            job.CreatedByUserId,
            ct);
    }

    [Fact]
    public async Task HandleAsync_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateVersionUploadJob(Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), Arg.Any<TextExtractionContext>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(ct);
        Assert.NotNull(version);
        Assert.Null(version.ExtractedText);
    }
}
