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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class UploadDocumentVersionJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly string _mediaPath;
    private readonly ITextExtractionEngine _textExtractionEngine;
    private readonly IProcessingNotifier _notifier;
    private readonly ITextExtractionEngineResolver _engineResolver;
    private readonly ThumbnailService _thumbnailService;
    private readonly ISender _sender;
    private readonly UploadDocumentVersionJobHandler _handler;

    public UploadDocumentVersionJobHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _mediaPath = Path.Combine(Path.GetTempPath(), $"upload-ver-test-{Guid.NewGuid()}");
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

        var logger = Substitute.For<ILogger<UploadDocumentVersionJobHandler>>();
        _thumbnailService = new ThumbnailService(
            Enumerable.Empty<IThumbnailGenerator>(), config,
            Substitute.For<ILogger<ThumbnailService>>());
        var serviceProvider = new ServiceCollection()
            .AddKeyedSingleton(TextExtractionEngineType.Tesseract, _textExtractionEngine)
            .BuildServiceProvider();
        var textExtractionHelper = new TextExtractionHelper(
            serviceProvider, _engineResolver);

        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new AddDocumentVersionResult(Guid.NewGuid(), "Existing Document"));

        _handler = new UploadDocumentVersionJobHandler(
            _db, config, logger,
            _notifier, _thumbnailService, textExtractionHelper, _sender);
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
        Guid? textExtractionEngineId = null,
        byte[]? content = null)
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
        File.WriteAllBytes(stagingPath, content ?? Encoding.UTF8.GetBytes($"unique version content {Guid.NewGuid()}"));

        return (job, stagingPath);
    }

    private static string HashOf(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    [Fact]
    public async Task HandleAsync_CallsEngineResolverWithPayloadEngineId()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var engineId = Guid.NewGuid();
        var (job, _) = CreateVersionUploadJob(doc.Id, textExtractionEngineId: engineId);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        await _handler.HandleAsync(job, ct);

        await _engineResolver.Received(1).ResolveAsync(null, ct);
    }

    [Fact]
    public async Task HandleAsync_SendsAddDocumentVersionCommand_WithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"version-fields {Guid.NewGuid()}");
        var (job, _) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("extracted text");

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<AddDocumentVersionCommand>(c =>
                c.DocumentId == doc.Id &&
                c.ExtractedText == "extracted text" &&
                c.FileHash == HashOf(content) &&
                c.Label == "v2" &&
                c.ActorUserId == job.CreatedByUserId),
            ct);
    }

    [Fact]
    public async Task HandleAsync_SendsSignalRNotification_WithResultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _sender.Send(Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>())
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
    public async Task HandleAsync_DocumentNotFound_Throws_AndDoesNotSend()
    {
        var ct = TestContext.Current.CancellationToken;
        var (job, _) = CreateVersionUploadJob(Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(job, ct));

        await _sender.DidNotReceive().Send(
            Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TextExtractionFails_ContinuesWithNullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var (job, _) = CreateVersionUploadJob(doc.Id);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns<string?>(x => throw new Exception("OCR failed"));

        await _handler.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<AddDocumentVersionCommand>(c => c.ExtractedText == null), ct);
    }

    [Fact]
    public async Task HandleAsync_MovesFileToFinalLocation_AndCleansUpStaging()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"move-final-ver {Guid.NewGuid()}");
        var (job, stagingPath) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        _sender.Send(Arg.Any<AddDocumentVersionCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<AddDocumentVersionResult>>(_ => throw new Exception("commit failed"));

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(job, ct));

        Assert.False(File.Exists(stagingPath));
        var finalPath = Path.Combine(_mediaPath, DocumentVersion.GetFilePath(HashOf(content), ".pdf"));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task HandleAsync_DuplicateHash_Throws_AndDoesNotSend()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SeedDocument();
        var content = Encoding.UTF8.GetBytes($"dupe-ver {Guid.NewGuid()}");
        var (job, _) = CreateVersionUploadJob(doc.Id, content: content);

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

        SeedExistingVersion(doc, HashOf(content));

        var ex = await Assert.ThrowsAsync<DuplicateDocumentException>(
            () => _handler.HandleAsync(job, ct));

        Assert.Equal(doc.Id, ex.ExistingDocumentId);
        Assert.Equal(doc.Name, ex.ExistingDocumentName);
        Assert.Equal("duplicate_content", ex.ErrorCode);
        Assert.False(ex.IsTrashed);
        Assert.Equal("false", ex.ErrorData["isTrashed"]);

        await _sender.DidNotReceive().Send(
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

        _textExtractionEngine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct)
            .Returns("text");

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
