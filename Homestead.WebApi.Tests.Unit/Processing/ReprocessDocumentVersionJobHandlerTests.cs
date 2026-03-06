using System.Text.Json;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class ReprocessDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractor _textExtractor;
    private readonly IProcessingNotifier _notifier;
    private readonly IAuditLogger _auditLogger;
    private readonly ISearchVectorUpdater _searchVectorUpdater;
    private readonly ReprocessDocumentVersionJobHandler _handler;

    public ReprocessDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"reprocess-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_mediaPath);

        _textExtractor = Substitute.For<ITextExtractor>();
        _textExtractor.CanExtract("application/pdf").Returns(true);

        _notifier = Substitute.For<IProcessingNotifier>();
        _auditLogger = Substitute.For<IAuditLogger>();
        _searchVectorUpdater = Substitute.For<ISearchVectorUpdater>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:MediaPath"] = _mediaPath
            })
            .Build();

        var logger = Substitute.For<ILogger<ReprocessDocumentVersionJobHandler>>();

        _handler = new ReprocessDocumentVersionJobHandler(
            _db, config, new[] { _textExtractor }, logger,
            _notifier, _auditLogger, _searchVectorUpdater);
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
    public async Task HandleAsync_ExtractsTextAndUpdatesVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("old text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), ct)
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

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), ct)
            .Returns("new text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.Received(1).LogUpdated(
            "DocumentVersion",
            version.Id,
            job.CreatedByUserId,
            Arg.Is<List<FieldChange>>(c => c.Count > 0),
            parentEntityId: doc.Id);
    }

    [Fact]
    public async Task HandleAsync_DoesNotCallAuditLoggerWhenTextUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("same text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), ct)
            .Returns("same text");

        await _handler.HandleAsync(job, ct);

        _auditLogger.DidNotReceive().LogUpdated(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(),
            Arg.Any<List<FieldChange>>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_UpdatesSearchVector()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion();
        var job = CreateJob(doc.Id, version.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), ct)
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

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), ct)
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
    public async Task HandleAsync_ThrowsWhenVersionNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));
    }

    [Fact]
    public async Task HandleAsync_HandlesTextExtractionFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var (doc, version) = SeedDocumentWithVersion("existing text");
        var job = CreateJob(doc.Id, version.Id);

        _textExtractor.ExtractTextAsync(Arg.Any<string>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        // ExtractedText should be set to null (extraction returned null due to catch)
        var updated = _db.DocumentVersions.First(v => v.Id == version.Id);
        Assert.Null(updated.ExtractedText);
    }
}
