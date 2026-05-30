using System.Security.Cryptography;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class UploadDocumentJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<UploadDocumentJobHandler> logger,
    IProcessingNotifier notifier,
    IDocumentClassifier classifier,
    ThumbnailService thumbnailService,
    TextExtractionHelper textExtractionHelper,
    ISender sender) : IJobHandler
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

            var documentName = payload.Name ?? Path.GetFileNameWithoutExtension(payload.OriginalFileName);

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

            // Auto-classify in-memory (non-fatal). Folded into creation below so the document is created
            // with AI values already applied — a single Created audit entry, no suppressed Updated.
            ClassificationResult? classification = null;
            Guid? providerId = null;
            try
            {
                if (await classifier.IsAutoClassifyEnabledAsync(ct))
                {
                    var output = await classifier.ClassifyAsync(
                        documentName, extractedText, aiProviderId: null, ClassificationScope.All, ct);
                    if (output is not null)
                    {
                        classification = output.Classification;
                        providerId = output.ProviderId;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-classification failed during upload {UploadId}", payload.UploadId);
            }

            // Persist document + first version (+ folded classification) in one unit of work.
            CreateDocumentFromUploadResult result;
            try
            {
                result = await sender.Send(new CreateDocumentFromUploadCommand
                {
                    Name = documentName,
                    GroupId = payload.GroupId,
                    ContentType = payload.ContentType,
                    FileSize = payload.FileSize,
                    FileHash = fileHash,
                    FileExtension = payload.FileExtension,
                    Label = payload.Label,
                    ExtractedText = extractedText,
                    Classification = classification,
                    ProviderId = providerId,
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

            // Update job with entity reference (committed with the job's terminal status by the processor)
            job.EntityId = result.DocumentId;

            // SignalR notification after the commit so the UI sees the AI-suggested name immediately
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                result.DocumentId,
                result.Name,
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
