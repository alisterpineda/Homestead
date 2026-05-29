using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Common.Auditing;
using System.Security.Cryptography;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class UploadDocumentJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<UploadDocumentJobHandler> logger,
    IProcessingNotifier notifier,
    IAuditLogger auditLogger,
    ISearchVectorUpdater searchVectorUpdater,
    IDocumentClassifier classifier,
    ThumbnailService thumbnailService,
    TextExtractionHelper textExtractionHelper) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var stagingPath = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        try
        {
            if (!File.Exists(stagingPath))
                throw new FileNotFoundException($"Staged file not found: {stagingPath}");

            // Text extraction
            var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
                stagingPath, payload.ContentType, payload.TextExtractionEngineId, ct);

            if (!extractionResult.Succeeded)
            {
                logger.LogWarning(extractionResult.Error,
                    "Text extraction failed for upload {UploadId}, proceeding without extracted text",
                    payload.UploadId);
            }

            var extractedText = extractionResult.Text;

            // Compute SHA256 hash
            string fileHash;
            await using (var stream = File.OpenRead(stagingPath))
            {
                var hashBytes = await SHA256.HashDataAsync(stream, ct);
                fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            // Duplicate check against all versions globally (IgnoreQueryFilters so trashed documents'
            // versions are still detected — matching the unique index IX_document_versions_FileHash)
            var existingMatch = await db.DocumentVersions
                .IgnoreQueryFilters()
                .Where(v => v.FileHash == fileHash)
                .Select(v => new { v.VersionNumber, v.Document.Id, v.Document.Name, v.Document.DeletedAt })
                .FirstOrDefaultAsync(ct);

            if (existingMatch is not null)
            {
                throw new DuplicateDocumentException(
                    "A document with the same content already exists.",
                    existingMatch.Id,
                    existingMatch.Name,
                    existingVersionNumber: existingMatch.VersionNumber,
                    isSameDocument: false,
                    isTrashed: existingMatch.DeletedAt != null);
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
                OwnerUserId = job.CreatedByUserId,
                GroupId = payload.GroupId
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
            db.Documents.Add(document);
            db.DocumentVersions.Add(version);

            auditLogger.ForUser(job.CreatedByUserId).LogCreated("Document", documentId);

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

            // Update job with entity reference
            job.EntityId = documentId;

            // Auto-classify if enabled (non-fatal — document is created regardless)
            var classified = false;
            try
            {
                if (await classifier.IsAutoClassifyEnabledAsync(ct))
                    classified = await classifier.ClassifyAndApplyAsync(
                        documentId, null, ClassificationScope.All, job.CreatedByUserId,
                        partOfCreation: true, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-classification failed for document {DocumentId}", documentId);
            }

            // ClassifyAndApplyAsync updates the search vector on success; only needed here if it didn't run
            if (!classified)
                await searchVectorUpdater.UpdateSearchVectorAsync(documentId, ct);

            // Reload to pick up any name changes from classification (or revert dirty state on partial failure)
            await db.Entry(document).ReloadAsync(ct);

            // Send SignalR notification after classification so the UI sees AI-suggested name/tags immediately
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                documentId,
                document.Name,
                error: null,
                job.CreatedByUserId,
                ct);

            // Generate thumbnail (non-critical, runs after notification)
            try
            {
                await thumbnailService.GenerateAsync(finalPath, payload.ContentType, fileHash, ct);
            }
            catch (OperationCanceledException) { throw; }
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
