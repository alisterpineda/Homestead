using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ReprocessDocumentVersionJobHandler(
    AppDbContext db,
    IConfiguration configuration,
    IProcessingNotifier notifier,
    IAuditLogger auditLogger,
    ISearchVectorUpdater searchVectorUpdater,
    TextExtractionHelper textExtractionHelper) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ReprocessDocumentVersion;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ReprocessDocumentVersionPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var version = await db.DocumentVersions
            .Include(v => v.Document)
            .FirstOrDefaultAsync(v => v.Id == payload.VersionId && v.DocumentId == payload.DocumentId, ct)
            ?? throw new InvalidOperationException($"DocumentVersion {payload.VersionId} not found.");

        var filePath = version.GetPhysicalPath(mediaPath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var before = DocumentVersionSnapshot.From(version);

        // Re-extract text
        var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
            filePath, version.ContentType, payload.TextExtractionEngineId, ct);

        if (!extractionResult.Succeeded)
        {
            throw new TextExtractionFailedException(
                $"Text extraction failed during reprocess of version {payload.VersionId}.",
                extractionResult.Error!);
        }

        version.ExtractedText = extractionResult.Text;

        var after = DocumentVersionSnapshot.From(version);
        var changes = before.Diff(after);

        if (changes.Count > 0)
        {
            auditLogger.ForUser(job.CreatedByUserId).LogUpdated("DocumentVersion", version.Id, changes,
                parentEntityId: version.DocumentId);
        }

        await db.SaveChangesAsync(ct);

        await searchVectorUpdater.UpdateSearchVectorAsync(version.DocumentId, ct);

        job.EntityId = payload.DocumentId;

        // Skip individual notification for child jobs — the parent job's
        // progress updates (via UpdateParentProgressAsync) handle notification.
        if (job.ParentJobId is null)
        {
            await notifier.NotifyJobStatusAsync(
                job.Id,
                ProcessingJobStatus.Completed,
                ProcessingEntityType.Document,
                payload.DocumentId,
                version.Document.Name,
                error: null,
                job.CreatedByUserId,
                ct);
        }
    }
}
