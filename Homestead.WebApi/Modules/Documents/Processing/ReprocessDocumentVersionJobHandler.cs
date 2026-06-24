using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Infrastructure.TextExtraction;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ReprocessDocumentVersionJobHandler(
    AppDbContext db,
    IOptions<FileStorageOptions> storage,
    IProcessingNotifier notifier,
    TextExtractionHelper textExtractionHelper,
    ISender sender) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ReprocessDocumentVersion;

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

        // Re-extract text (the failure-prone step stays in the job handler)
        var extractionResult = await textExtractionHelper.ExtractTextSafeAsync(
            filePath, version.ContentType, ct);

        if (!extractionResult.Succeeded)
        {
            throw new TextExtractionFailedException(
                $"Text extraction failed during reprocess of version {payload.VersionId}.",
                extractionResult.Error!);
        }

        // Persist the re-extracted text + audit in one unit of work.
        var result = await sender.Send(new ReprocessVersionTextCommand
        {
            DocumentId = payload.DocumentId,
            VersionId = payload.VersionId,
            ExtractedText = extractionResult.Text,
            ActorUserId = job.CreatedByUserId
        }, ct);

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
