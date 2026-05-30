using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Processing;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class ClassifyDocumentJobHandler(
    IProcessingNotifier notifier,
    ISender sender) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.ClassifyDocument;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = job.GetPayload<ClassifyDocumentPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        job.EntityId = payload.DocumentId;

        var result = await sender.Send(new ApplyClassificationCommand
        {
            DocumentId = payload.DocumentId,
            AiProviderId = payload.AiProviderId,
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
