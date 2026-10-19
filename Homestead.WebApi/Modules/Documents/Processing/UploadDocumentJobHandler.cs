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

public class UploadDocumentJobHandler(
    AppDbContext db,
    IOptions<FileStorageOptions> storage,
    ILogger<UploadDocumentJobHandler> logger,
    IProcessingNotifier notifier,
    IDocumentClassifier classifier,
    ThumbnailService thumbnailService,
    TextExtractionHelper textExtractionHelper,
    ITextExtractionEngineResolver engineResolver,
    IUnitOfWork unitOfWork,
    ICreateDocumentFromUploadHandler createDocument) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = storage.Value.MediaPath;

        var stagingPath = Path.Combine(mediaPath, DocumentVersion.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        try
        {
            if (!File.Exists(stagingPath))
                throw new FileNotFoundException($"Staged file not found: {stagingPath}");

            // Text extraction. Uploads have no explicit pick — nobody is at a dialog choosing an engine —
            // so they take the configured default (Ocr:DefaultEngine), which falls back to the built-in
            // engine if it names nothing usable (recorded in the version's provenance metadata).
            var engineKey = engineResolver.DefaultKey;
            var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
                stagingPath, payload.ContentType, engineKey, ct);

            if (!extractionResult.Succeeded)
            {
                logger.LogWarning(extractionResult.Error,
                    "Text extraction failed for upload {UploadId}, proceeding without extracted text",
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
                throw new DuplicateDocumentException(
                    "A document with the same content already exists.",
                    existingMatch.DocumentId,
                    existingMatch.DocumentName,
                    existingVersionNumber: existingMatch.VersionNumber,
                    isSameDocument: false,
                    isTrashed: existingMatch.IsTrashed);
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
            try
            {
                if (await classifier.IsAutoClassifyEnabledAsync(ct))
                {
                    // Hand the placed file to the in-memory classify overload, since the document/version doesn't
                    // exist yet. The classifier gates on AI being available before touching it (so an unconfigured
                    // app makes no render and reads no PDF — IsAutoClassifyEnabledAsync only reads the setting),
                    // then sends the PDF itself or a page-1 render (scope is All); a render also warms the
                    // hash+edge-keyed cache for any later re-classify.
                    var output = await classifier.ClassifyAsync(
                        documentName, extractedText,
                        new ClassificationFile(fileHash, finalPath, payload.ContentType, payload.FileSize),
                        ClassificationScope.All, ct);
                    if (output is not null)
                        classification = output.Classification;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-classification failed during upload {UploadId}", payload.UploadId);
            }

            // Persist document + first version (+ folded classification) in one unit of work, attributed
            // to the uploader.
            var command = new CreateDocumentFromUploadCommand
            {
                Name = documentName,
                GroupId = payload.GroupId,
                ContentType = payload.ContentType,
                FileSize = payload.FileSize,
                FileHash = fileHash,
                FileExtension = payload.FileExtension,
                Label = payload.Label,
                ExtractedText = extractedText,
                TextExtractionMetadata = textExtractionMetadata,
                Classification = classification,
                ActorUserId = job.CreatedByUserId
            };
            CreateDocumentFromUploadResult result;
            try
            {
                unitOfWork.SetActor(job.CreatedByUserId);
                result = await unitOfWork.ExecuteInTransactionAsync(() => createDocument.HandleAsync(command, ct), ct);
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
