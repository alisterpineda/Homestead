using System.Security.Cryptography;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class UploadDocumentVersionJobHandler(
    AppDbContext db,
    IOptions<FileStorageOptions> storage,
    ILogger<UploadDocumentVersionJobHandler> logger,
    IProcessingNotifier notifier,
    ThumbnailService thumbnailService,
    TextExtractionHelper textExtractionHelper,
    ITextExtractionEngineResolver engineResolver,
    IUnitOfWork unitOfWork,
    IAddDocumentVersionHandler addDocumentVersion) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocumentVersion;

    // Uploads run the configured default engine, so they get its budget (Ocr:Engines:{key}:JobTimeout).
    // Resolved per run like the key itself: an engine change applies to the next upload.
    public TimeSpan? ResolveTimeout(ProcessingJob job) =>
        textExtractionHelper.JobTimeoutFor(engineResolver.DefaultKey);

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentVersionPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = storage.Value.MediaPath;

        // Fail early (before extraction/file move) if the parent document is gone
        var documentExists = await db.Documents.AnyAsync(d => d.Id == payload.DocumentId, ct);
        if (!documentExists)
            throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

        var stagingPath = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        try
        {
            if (!File.Exists(stagingPath))
                throw new FileNotFoundException($"Staged file not found: {stagingPath}");

            // Text extraction on the configured default (Ocr:DefaultEngine) — see UploadDocumentJobHandler.
            var engineKey = engineResolver.DefaultKey;
            var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
                stagingPath, payload.ContentType, engineKey, ct);

            if (!extractionResult.Succeeded)
            {
                logger.LogWarning(extractionResult.Error,
                    "Text extraction failed for version upload {UploadId}, proceeding without extracted text",
                    payload.UploadId);
            }

            var extractedText = extractionResult.Text;
            var textExtractionMetadata = TextExtractionMetadata.Serialize(extractionResult.Metadata);

            // Compute SHA256 hash
            string fileHash;
            await using (var stream = File.OpenRead(stagingPath))
            {
                var hashBytes = await SHA256.HashDataAsync(stream, ct);
                fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            var existingMatch = await VersionDuplicateCheck.FindByHashAsync(db, fileHash, ct);

            if (existingMatch is not null)
            {
                var isSameDocument = existingMatch.DocumentId == payload.DocumentId;
                var message = isSameDocument
                    ? $"This file is identical to v{existingMatch.VersionNumber} of the document — nothing has changed."
                    : "A document with the same content already exists.";

                throw new DuplicateDocumentException(
                    message,
                    existingMatch.DocumentId,
                    existingMatch.DocumentName,
                    existingVersionNumber: existingMatch.VersionNumber,
                    isSameDocument: isSameDocument,
                    isTrashed: existingMatch.IsTrashed);
            }

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

            // Persist the new version in one unit of work, attributed to the job's creator.
            var command = new AddDocumentVersionCommand
            {
                DocumentId = payload.DocumentId,
                ContentType = payload.ContentType,
                FileSize = payload.FileSize,
                FileHash = fileHash,
                FileExtension = payload.FileExtension,
                Label = payload.Label,
                ExtractedText = extractedText,
                TextExtractionMetadata = textExtractionMetadata
            };
            AddDocumentVersionResult result;
            try
            {
                unitOfWork.SetActor(job.CreatedByUserId);
                result = await unitOfWork.ExecuteInTransactionAsync(() => addDocumentVersion.HandleAsync(command, ct), ct);
            }
            catch
            {
                // Compensation: remove the file we just placed (the DB never committed)
                if (filePlaced)
                    try { File.Delete(finalPath); } catch { }
                throw;
            }

            // Update job with entity reference (parent document)
            job.EntityId = payload.DocumentId;

            // Send SignalR notification before thumbnail generation so the UI sees the upload as complete immediately
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                payload.DocumentId,
                result.DocumentName,
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
