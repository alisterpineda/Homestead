using Homestead.WebApi.Common.Auditing;
using System.Security.Cryptography;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
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

        try
        {
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

            // Duplicate check against all versions globally (IgnoreQueryFilters so trashed documents'
            // versions are still detected — matching the unique index IX_document_versions_FileHash)
            var existingDocument = await db.DocumentVersions
                .IgnoreQueryFilters()
                .Where(v => v.FileHash == fileHash)
                .Select(v => new { v.Document.Id, v.Document.Name, v.Document.DeletedAt })
                .FirstOrDefaultAsync(ct);

            if (existingDocument is not null)
            {
                throw new DuplicateDocumentException(
                    "A document with the same content already exists.",
                    existingDocument.Id,
                    existingDocument.Name,
                    isTrashed: existingDocument.DeletedAt != null);
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

            // Move file to final location FIRST (Copy+Delete for cross-filesystem safety)
            var finalPath = Path.Combine(mediaPath, DocumentVersion.GetFilePath(fileHash, payload.FileExtension));
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            var filePlaced = false;
            if (!File.Exists(finalPath))
            {
                File.Copy(stagingPath, finalPath, overwrite: false);
                File.Delete(stagingPath);
                filePlaced = true;
            }

            // Then commit DB
            db.DocumentVersions.Add(version);

            auditLogger.ForUser(job.CreatedByUserId).LogCreated("DocumentVersion", version.Id, parentEntityId: payload.DocumentId);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                // Compensation: remove the file we just placed
                if (filePlaced)
                    try { File.Delete(finalPath); } catch { }
                throw;
            }

            await searchVectorUpdater.UpdateSearchVectorAsync(payload.DocumentId, ct);

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
        finally
        {
            try { File.Delete(stagingPath); } catch { }
        }
    }
}
