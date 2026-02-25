using System.Security.Cryptography;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Persistence;
using HomeSuiteHome.WebApi.Processing;

namespace HomeSuiteHome.WebApi.Features.Documents.Processing;

public class UploadDocumentJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    IProcessingNotifier notifier) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var stagingPath = Path.Combine(mediaPath, Document.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

        if (!File.Exists(stagingPath))
            throw new FileNotFoundException($"Staged file not found: {stagingPath}");

        // TODO: Remove this delay once real processing work (OCR, rule evaluation, etc.) is added.
        // It exists only to make the loading → success toast transition visible during development.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        // Compute SHA256 hash
        string fileHash;
        await using (var stream = File.OpenRead(stagingPath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // Create document entity
        var documentId = Guid.NewGuid();
        var documentName = payload.Name ?? payload.OriginalFileName;

        var document = new Document
        {
            Id = documentId,
            Name = documentName,
            ContentType = payload.ContentType,
            FileSize = payload.FileSize,
            FilePath = Document.GetFilePath(documentId, payload.FileExtension),
            FileHash = fileHash,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = job.CreatedByUserId,
            OwnerUserId = job.CreatedByUserId
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        // Move file to final location
        var finalDir = Path.Combine(mediaPath, Document.FilesSubroot);
        Directory.CreateDirectory(finalDir);
        var finalPath = Path.Combine(finalDir, $"{documentId}{payload.FileExtension}");
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
