using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class ReprocessDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;
    private readonly IProcessingNotifier _notifier;
    private readonly IAuditLogger _auditLogger;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly ITextExtractionEngineResolver _engineResolver;
    private readonly ReprocessDocumentVersionJobHandler _handler;

    public ReprocessDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"reprocess-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_mediaPath);

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
                Homestead.WebApi.Domain.Settings.TextExtractionEngineType.Tesseract,
                Homestead.WebApi.Domain.Settings.BuiltInTextExtractionEngines.TesseractDefaultConfiguration));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:MediaPath"] = _mediaPath
            })
            .Build();

        var serviceProvider = new ServiceCollection()
            .AddKeyedSingleton(Homestead.WebApi.Domain.Settings.TextExtractionEngineType.Tesseract, _textExtractionEngine)
            .BuildServiceProvider();
        var textExtractionHelper = new TextExtractionHelper(
            serviceProvider, _engineResolver);

        _handler = new ReprocessDocumentVersionJobHandler(
            _db, config,
            _notifier, _auditLogger, _searchVectorUpdater, textExtractionHelper);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_mediaPath, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (Document doc, DocumentVersion version) SeedDocumentWithVersion(string? extractedText = "old text")
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
            ContentType = "application/pdf",
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

        // Create the physical file
        var physicalPath = Path.Combine(_mediaPath, filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        File.WriteAllText(physicalPath, "fake pdf content");

        return (doc, version);
    }

    private ProcessingJob CreateJob(Guid documentId, Guid versionId, Guid? textExtractionEngineId = null)
    {
        var payload = new ReprocessDocumentVersionPayload(documentId, versionId, textExtractionEngineId);
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
    public async Task HandleAsync_ExtractsTextAndUpdatesVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("new extracted text");

        await _handler.HandleAsync(job, ct);

        var updated = _db.DocumentVersions.First(v => v.Id == version.Id);
        Assert.Equal("new extracted text", updated.ExtractedText);
    }

    [Fact]
    public async Task HandleAsync_CallsAuditLoggerWhenTextChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("new text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.Received(1).ForUser(job.CreatedByUserId);
        _auditLogger.Received(1).LogUpdated(
            "DocumentVersion",
            version.Id,
            Arg.Is<List<FieldChange>>(c => c.Count > 0),
            parentEntityId: doc.Id);
    }

    [Fact]
    public async Task HandleAsync_DoesNotCallAuditLoggerWhenTextUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("same text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("same text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.DidNotReceive().LogUpdated(
            Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<List<FieldChange>>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_UpdatesSearchVector()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _searchVectorUpdater.Received(1).UpdateSearchVectorAsync(doc.Id, ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _notifier.Received(1).NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            doc.Id,
            doc.Name,
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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _notifier.DidNotReceive().NotifyJobStatusAsync(
            Arg.Any<Guid>(), Arg.Any<ProcessingJobStatus>(),
            Arg.Any<ProcessingEntityType?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>(),
            Arg.Any<string?>(), Arg.Any<Dictionary<string, string>?>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task HandleAsync_ThrowsWhenVersionNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_PreservesExistingText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await Assert.ThrowsAsync<TextExtractionFailedException>(
            () => _handler.HandleAsync(job, ct));

        var updated = _db.DocumentVersions.First(v => v.Id == version.Id);
        Assert.Equal("existing text", updated.ExtractedText);
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ErrorImplementsIStructuredJobError()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        var ex = await Assert.ThrowsAsync<TextExtractionFailedException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal("text_extraction_failed", ex.ErrorCode);
        Assert.Equal("OCR failed", ex.ErrorData["innerError"]);
    }

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithPayloadEngineId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var engineId = Guid.NewGuid();
        var job = CreateJob(doc.Id, version.Id, textExtractionEngineId: engineId);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("new text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(engineId, ct);
    }

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithNull_WhenNoEngineIdInPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var job = CreateJob(doc.Id, version.Id, textExtractionEngineId: null);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("new text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(null, ct);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedContentType_ExtractsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");

        // Change content type to something without an extractor
        version.ContentType = "text/plain";
        await _db.SaveChangesAsync(ct);

        var job = CreateJob(doc.Id, version.Id);

        await _handler.HandleAsync(job, ct);

        var updated = _db.DocumentVersions.First(v => v.Id == version.Id);
        Assert.Null(updated.ExtractedText);
    }
}
