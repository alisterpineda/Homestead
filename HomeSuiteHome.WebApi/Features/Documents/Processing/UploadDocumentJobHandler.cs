using System.Security.Cryptography;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Persistence;
using HomeSuiteHome.WebApi.Processing;
using HomeSuiteHome.WebApi.TextExtraction;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.Processing;

public class UploadDocumentJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    IEnumerable<ITextExtractor> textExtractors,
    ILogger<UploadDocumentJobHandler> logger,
    IProcessingNotifier notifier) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var stagingPath = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        if (!File.Exists(stagingPath))
            throw new FileNotFoundException($"Staged file not found: {stagingPath}");

        // Text extraction (OCR for images)
        string? extractedText = null;
        var extractor = textExtractors.FirstOrDefault(e => e.CanExtract(payload.ContentType));
        if (extractor is not null)
        {
            try
            {
                extractedText = await extractor.ExtractTextAsync(stagingPath, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Text extraction failed for upload {UploadId}", payload.UploadId);
            }
        }

        // Compute SHA256 hash
        string fileHash;
        await using (var stream = File.OpenRead(stagingPath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // Duplicate check against versions
        var isDuplicate = await db.DocumentVersions.AnyAsync(r => r.FileHash == fileHash, ct);
        if (isDuplicate)
        {
            File.Delete(stagingPath);
            throw new InvalidOperationException("A document with the same content already exists.");
        }

        // Create document entity
        var documentId = Guid.NewGuid();
        var documentName = payload.Name ?? Path.GetFileNameWithoutExtension(payload.OriginalFileName);

        var document = new Document
        {
            Id = documentId,
            Name = documentName,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = job.CreatedByUserId,
            OwnerUserId = job.CreatedByUserId
        };

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = 1,
            Label = payload.Label,
            ContentType = payload.ContentType,
            FileSize = payload.FileSize,
            FilePath = DocumentVersion.GetFilePath(fileHash, payload.FileExtension),
            FileHash = fileHash,
            FileExtension = payload.FileExtension,
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = documentId,
            ExtractedText = extractedText
        };

        db.Documents.Add(document);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(ct);

        // Move file to final location
        var finalPath = Path.Combine(mediaPath, DocumentVersion.GetFilePath(fileHash, payload.FileExtension));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(stagingPath, finalPath);

        // Update job with entity reference
        job.EntityId = documentId;

        // Send SignalR notification
        await notifier.NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            documentId,
            documentName,
            error: null,
            job.CreatedByUserId,
            ct);
    }
}
