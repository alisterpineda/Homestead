using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ClassifyDocumentJobHandler(
    AppDbContext db,
    IDocumentClassifier classifier,
    IProcessingNotifier notifier) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ClassifyDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ClassifyDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        job.EntityId = payload.DocumentId;

        await classifier.ClassifyAndApplyAsync(
            payload.DocumentId, payload.AiProviderId, payload.Scope, job.CreatedByUserId, ct);

        if (job.ParentJobId is null)
        {
            var document = await db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == payload.DocumentId, ct)
                ?? throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

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
}
