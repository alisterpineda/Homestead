using System.Diagnostics;
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
    ICreateDocumentFromUploadHandler createDocument,
    IOptions<JobProcessingOptions> processing) : IJobHandler
{
    // Left over once auto-classify's time is up, for creating the document and notifying. Capped at a
    // tenth of the budget so a short one still leaves classification most of what remains.
    internal static readonly TimeSpan PersistReserve = TimeSpan.FromSeconds(10);

    public ProcessingJobType Type => ProcessingJobType.UploadDocument;

    // Uploads run the configured default engine, so they get its budget (Ocr:Engines:{key}:JobTimeout).
    // Resolved per run like the key itself: an engine change applies to the next upload.
    public TimeSpan? ResolveTimeout(ProcessingJob job) =>
        textExtractionHelper.JobTimeoutFor(engineResolver.DefaultKey);

    // How long auto-classify may take: what is left of the job budget, less the persist reserve. Zero or
    // less means there is no time left to try.
    internal static TimeSpan ClassificationAllowance(TimeSpan budget, TimeSpan elapsed)
    {
        var reserve = PersistReserve < budget / 10 ? PersistReserve : budget / 10;
        return budget - elapsed - reserve;
    }

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        // The runner started this job's clock just before calling in, under the same budget (see
        // ResolveTimeout); classification below is bounded against it.
        var started = Stopwatch.GetTimestamp();
        var budget = ResolveTimeout(job) ?? processing.Value.ResolveJobTimeout();

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
            //
            // Bounded by what is left of the job budget. Extraction may already have run to its own
            // deadline, and a classify that then outran the job would time the whole upload out with no
            // document; running out of time here is just a failed classification instead.
            ClassificationResult? classification = null;
            var allowance = TimeSpan.Zero;
            using var classifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                if (await classifier.IsAutoClassifyEnabledAsync(ct))
                {
                    allowance = ClassificationAllowance(budget, Stopwatch.GetElapsedTime(started));
                    if (allowance > TimeSpan.Zero)
                    {
                        classifyCts.CancelAfter(allowance);

                        // Hand the placed file to the in-memory classify overload, since the document/version
                        // doesn't exist yet. The classifier gates on AI being available before touching it (so an
                        // unconfigured app makes no render and reads no PDF — IsAutoClassifyEnabledAsync only reads
                        // the setting), then sends the PDF itself or a page-1 render (scope is All); a render also
                        // warms the hash+edge-keyed cache for any later re-classify.
                        var output = await classifier.ClassifyAsync(
                            documentName, extractedText,
                            new ClassificationFile(fileHash, finalPath, payload.ContentType, payload.FileSize),
                            ClassificationScope.All, classifyCts.Token);
                        if (output is not null)
                            classification = output.Classification;
                    }
                    else
                    {
                        logger.LogWarning(
                            "No time left in the job budget ({Budget}) to auto-classify upload {UploadId}; creating it unclassified",
                            budget, payload.UploadId);
                    }
                }
            }
            catch (OperationCanceledException) when (classifyCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Auto-classification of upload {UploadId} did not finish within the {Allowance} left in the job budget; creating it unclassified",
                    payload.UploadId, allowance);
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
