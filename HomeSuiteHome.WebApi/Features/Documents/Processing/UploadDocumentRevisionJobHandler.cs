using System.Security.Cryptography;
using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Persistence;
using HomeSuiteHome.WebApi.Processing;
using HomeSuiteHome.WebApi.TextExtraction;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.Processing;

public class UploadDocumentRevisionJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    IEnumerable<ITextExtractor> textExtractors,
    ILogger<UploadDocumentRevisionJobHandler> logger,
    IProcessingNotifier notifier) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.UploadDocumentRevision;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<UploadDocumentRevisionPayload>()
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

        var stagingPath = Path.Combine(mediaPath, DocumentRevision.UploadsSubroot, $"{payload.UploadId}{payload.FileExtension}");

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

        // Duplicate check against all revisions globally
        var isDuplicate = await db.DocumentRevisions.AnyAsync(r => r.FileHash == fileHash, ct);
        if (isDuplicate)
        {
            File.Delete(stagingPath);
            throw new InvalidOperationException("A revision with the same file content already exists.");
        }

        // Compute next revision number
        var maxRevisionNumber = await db.DocumentRevisions
            .Where(r => r.DocumentId == payload.DocumentId)
            .MaxAsync(r => (int?)r.RevisionNumber, ct) ?? 0;

        var revision = new DocumentRevision
        {
            Id = Guid.NewGuid(),
            RevisionNumber = maxRevisionNumber + 1,
            Label = payload.Label,
            Notes = payload.Notes,
            ContentType = payload.ContentType,
            FileSize = payload.FileSize,
            FilePath = DocumentRevision.GetFilePath(fileHash, payload.FileExtension),
            FileHash = fileHash,
            FileExtension = payload.FileExtension,
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = payload.DocumentId,
            ExtractedText = extractedText
        };

        db.DocumentRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        // Move file to final location
        var finalPath = Path.Combine(mediaPath, DocumentRevision.GetFilePath(fileHash, payload.FileExtension));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(stagingPath, finalPath);

        // Update job with entity reference (parent document)
        job.EntityId = payload.DocumentId;

        // Send SignalR notification
        await notifier.NotifyJobStatusAsync(
            job.Id,
            ProcessingJobStatus.Completed,
            ProcessingEntityType.Document,
            payload.DocumentId,
            document.Name,
            error: null,
            job.CreatedByUserId,
            ct);
    }
}
