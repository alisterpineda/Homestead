using System.Text.Json;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class BulkClassifyDocumentsJobHandler(
    AppDbContext db,
    IJobSignal signal) : IParentJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.BulkClassifyDocuments;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        var existingChildCount = await db.ProcessingJobs.CountAsync(j => j.ParentJobId == job.Id, ct);
        if (existingChildCount > 0)
        {
            job.TotalChildCount = existingChildCount;
            return;
        }

        var payload = job.GetPayload<BulkClassifyPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var documentIds = await db.Documents
            .Where(d => payload.DocumentIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);

        foreach (var documentId in documentIds)
        {
            var childPayload = new ClassifyDocumentPayload(documentId, payload.Scope);

            db.ProcessingJobs.Add(new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.ClassifyDocument,
                Status = ProcessingJobStatus.Pending,
                EntityType = ProcessingEntityType.Document,
                EntityId = documentId,
                CreatedByUserId = job.CreatedByUserId,
                ParentJobId = job.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonSerializer.Serialize(childPayload),
            });
        }

        job.TotalChildCount = documentIds.Count;
        await db.SaveChangesAsync(ct);
        signal.Signal();
    }
}
