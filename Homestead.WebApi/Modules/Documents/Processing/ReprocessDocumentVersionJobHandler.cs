using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ReprocessDocumentVersionJobHandler(
    AppDbContext db,
    IOptions<FileStorageOptions> storage,
    IProcessingNotifier notifier,
    TextExtractionHelper textExtractionHelper,
    IUnitOfWork unitOfWork,
    IReprocessVersionTextHandler reprocessVersionText) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ReprocessDocumentVersion;

    // The engine stamped into the payload decides the budget. A payload that does not parse gets the
    // global one; HandleAsync then fails it properly.
    public TimeSpan? ResolveTimeout(ProcessingJob job) =>
        job.GetPayload<ReprocessDocumentVersionPayload>()?.EngineKey is { } key
            ? textExtractionHelper.JobTimeoutFor(key)
            : null;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ReprocessDocumentVersionPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var mediaPath = storage.Value.MediaPath;

        var version = await db.DocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == payload.VersionId && v.DocumentId == payload.DocumentId, ct)
            ?? throw new InvalidOperationException($"DocumentVersion {payload.VersionId} not found.");

        var filePath = version.GetPhysicalPath(mediaPath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // Re-extract text (the failure-prone step stays in the job handler). The engine key was resolved
        // and stamped into the payload at enqueue, so a bulk run cannot split across engines if the
        // configured default flips mid-run, and this path needs no settings read.
        var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
            filePath, version.ContentType, payload.EngineKey, ct);

        if (!extractionResult.Succeeded)
        {
            throw new TextExtractionFailedException(
                $"Text extraction failed during reprocess of version {payload.VersionId}.",
                extractionResult.Error!);
        }

        // Persist the re-extracted text + audit in one unit of work, attributed to the job's creator.
        var command = new ReprocessVersionTextCommand
        {
            DocumentId = payload.DocumentId,
            VersionId = payload.VersionId,
            ExtractedText = extractionResult.Text,
            TextExtractionMetadata = TextExtractionMetadata.Serialize(extractionResult.Metadata)
        };
        unitOfWork.SetActor(job.CreatedByUserId);
        var result = await unitOfWork.ExecuteInTransactionAsync(() => reprocessVersionText.HandleAsync(command, ct), ct);

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
                result.DocumentName,
                error: null,
                job.CreatedByUserId,
                ct);
        }
    }
}
