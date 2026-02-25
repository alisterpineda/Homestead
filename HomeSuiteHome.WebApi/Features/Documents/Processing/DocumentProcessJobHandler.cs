using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Processing;

namespace HomeSuiteHome.WebApi.Features.Documents.Processing;

public class DocumentProcessJobHandler(ILogger<DocumentProcessJobHandler> logger) : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.DocumentProcess;

    public Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        logger.LogInformation("Processing document {EntityId} (job {JobId})", job.EntityId, job.Id);
        // Placeholder: future steps will include OCR, rule evaluation, etc.
        return Task.CompletedTask;
    }
}
