using System.Security.Cryptography;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.TextExtraction;
using Homestead.WebApi.Thumbnails;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Processing;

public class UploadDocumentVersionJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<UploadDocumentVersionJobHandler> logger,
    IProcessingNotifier notifier,
    IAuditLogger auditLogger,
    ISearchVectorUpdater searchVectorUpdater,
    ThumbnailService thumbnailService,
    TextExtractionHelper textExtractionHelper) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocumentVersion;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentVersionPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        // Verify document exists
        var document = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == payload.DocumentId)
            .Select(d => new { d.Id, d.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

        var stagingPath = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        if (!File.Exists(stagingPath))
            throw new FileNotFoundException($"Staged file not found: {stagingPath}");

        // Text extraction
        var extractedText = await textExtractionHelper.ExtractTextSafeAsync(
            stagingPath, payload.ContentType, payload.TextExtractionEngineId, logger, ct);

        // Compute SHA256 hash
        string fileHash;
        await using (var stream = File.OpenRead(stagingPath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // Duplicate check against all versions globally
        var isDuplicate = await db.DocumentVersions.AnyAsync(r => r.FileHash == fileHash, ct);
        if (isDuplicate)
        {
            File.Delete(stagingPath);
            throw new InvalidOperationException("A version with the same file content already exists.");
        }

        // Compute next version number
        var maxVersionNumber = await db.DocumentVersions
            .Where(r => r.DocumentId == payload.DocumentId)
            .MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0;

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VersionNumber = maxVersionNumber + 1,
            Label = payload.Label,
            ContentType = payload.ContentType,
            FileSize = payload.FileSize,
            FilePath = DocumentVersion.GetFilePath(fileHash, payload.FileExtension),
            FileHash = fileHash,
            FileExtension = payload.FileExtension,
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = payload.DocumentId,
            ExtractedText = extractedText
        };

        db.DocumentVersions.Add(version);

        auditLogger.LogCreated("DocumentVersion", version.Id, job.CreatedByUserId, parentEntityId: payload.DocumentId);

        await db.SaveChangesAsync(ct);

        await searchVectorUpdater.UpdateSearchVectorAsync(payload.DocumentId, ct);

        // Move file to final location
        var finalPath = Path.Combine(mediaPath, DocumentVersion.GetFilePath(fileHash, payload.FileExtension));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(stagingPath, finalPath);

        // Update job with entity reference (parent document)
        job.EntityId = payload.DocumentId;

        // Send SignalR notification before thumbnail generation so the UI sees the upload as complete immediately
        await notifier.NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            payload.DocumentId,
            document.Name,
            error: null,
            job.CreatedByUserId,
            ct);

        // Generate thumbnail (non-critical, runs after notification)
        try
        {
            await thumbnailService.GenerateAsync(finalPath, payload.ContentType, fileHash, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Thumbnail generation failed for upload {UploadId}", payload.UploadId);
        }
    }
}
