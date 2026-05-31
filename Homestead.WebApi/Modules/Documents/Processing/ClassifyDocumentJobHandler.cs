using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ClassifyDocumentJobHandler(
    AppDbContext db,
    IDocumentClassifier classifier,
    IProcessingNotifier notifier,
    ISender sender) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ClassifyDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ClassifyDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        job.EntityId = payload.DocumentId;

        // Read the document name here so the slow AI classification runs in the job scope (no ambient
        // transaction), not inside the command transaction. Reading it at processing time also avoids
        // adding Name to the payload — no backward-compat concern for in-flight queued jobs.
        var documentName = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == payload.DocumentId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Document {payload.DocumentId} not found.");

        var output = await classifier.ClassifyAsync(
            payload.DocumentId, documentName, payload.AiProviderId, payload.Scope, ct);

        // Dispatch the thinned command carrying the precomputed result — the handler only persists.
        var result = await sender.Send(new ApplyClassificationCommand
        {
            DocumentId = payload.DocumentId,
            Classification = output?.Classification,
            ProviderId = output?.ProviderId,
            Scope = payload.Scope,
            ActorUserId = job.CreatedByUserId
        }, ct);

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
