using System.Security.Cryptography;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.TextExtraction;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Processing;

public class UploadDocumentVersionJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    IEnumerable<ITextExtractor> textExtractors,
    ILogger<UploadDocumentVersionJobHandler> logger,
    IProcessingNotifier notifier) : IJobHandler
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

        AuditLogger.LogCreated(db, "DocumentVersion", version.Id, job.CreatedByUserId, parentEntityId: payload.DocumentId);

        await db.SaveChangesAsync(ct);

        await SearchVectorHelper.UpdateSearchVectorAsync(db, payload.DocumentId, ct);

        // Move file to final location
        var finalPath = Path.Combine(mediaPath, DocumentVersion.GetFilePath(fileHash, payload.FileExtension));
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
