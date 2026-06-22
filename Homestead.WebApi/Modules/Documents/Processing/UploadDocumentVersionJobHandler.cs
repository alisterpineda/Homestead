using System.Security.Cryptography;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class UploadDocumentVersionJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<UploadDocumentVersionJobHandler> logger,
    IProcessingNotifier notifier,
    ThumbnailService thumbnailService,
    TextExtractionHelper textExtractionHelper,
    ISender sender) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocumentVersion;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentVersionPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        // Fail early (before extraction/file move) if the parent document is gone
        var documentExists = await db.Documents.AnyAsync(d => d.Id == payload.DocumentId, ct);
        if (!documentExists)
            throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

        var stagingPath = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        try
        {
            if (!File.Exists(stagingPath))
                throw new FileNotFoundException($"Staged file not found: {stagingPath}");

            // Text extraction
            var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
                stagingPath, payload.ContentType, ct);

            if (!extractionResult.Succeeded)
            {
                logger.LogWarning(extractionResult.Error,
                    "Text extraction failed for version upload {UploadId}, proceeding without extracted text",
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
                var isSameDocument = existingMatch.Id == payload.DocumentId;
                var message = isSameDocument
                    ? $"This file is identical to v{existingMatch.VersionNumber} of the document — nothing has changed."
                    : "A document with the same content already exists.";

                throw new DuplicateDocumentException(
                    message,
                    existingMatch.Id,
                    existingMatch.Name,
                    existingVersionNumber: existingMatch.VersionNumber,
                    isSameDocument: isSameDocument,
                    isTrashed: existingMatch.DeletedAt != null);
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

            // Persist the new version in one unit of work.
            AddDocumentVersionResult result;
            try
            {
                result = await sender.Send(new AddDocumentVersionCommand
                {
                    DocumentId = payload.DocumentId,
                    ContentType = payload.ContentType,
                    FileSize = payload.FileSize,
                    FileHash = fileHash,
                    FileExtension = payload.FileExtension,
                    Label = payload.Label,
                    ExtractedText = extractedText,
                    ActorUserId = job.CreatedByUserId
                }, ct);
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
