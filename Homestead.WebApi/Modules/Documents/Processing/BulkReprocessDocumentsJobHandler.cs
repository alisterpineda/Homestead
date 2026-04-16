using System.Text.Json;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Processing;

public class BulkReprocessDocumentsJobHandler(
    AppDbContext db,
    IJobSignal signal) : IParentJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.BulkReprocessDocuments;

    public async Task HandleAsync(ProcessingJob job, CancellationToken ct)
    {
        // Idempotency: if children already exist (crash recovery), restore TotalChildCount and skip
        var existingChildCount = await db.ProcessingJobs.CountAsync(j => j.ParentJobId == job.Id, ct);
        if (existingChildCount > 0)
        {
            job.TotalChildCount = existingChildCount;
            return;
        }

        var payload = job.GetPayload<BulkReprocessPayload>()
            ?? throw new InvalidOperationException("Job payload is missing or invalid.");

        var latestVersions = await db.DocumentVersions
            .Where(v => payload.DocumentIds.Contains(v.DocumentId))
            .GroupBy(v => v.DocumentId)
            .Select(g => new
            {
                DocumentId = g.Key,
                VersionId = g.OrderByDescending(v => v.VersionNumber).Select(v => v.Id).First(),
            })
            .ToListAsync(ct);

        foreach (var item in latestVersions)
        {
            var childPayload = new ReprocessDocumentVersionPayload(
                item.DocumentId, item.VersionId, payload.TextExtractionEngineId);

            db.ProcessingJobs.Add(new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.ReprocessDocumentVersion,
                Status = ProcessingJobStatus.Pending,
                EntityType = ProcessingEntityType.Document,
                EntityId = item.DocumentId,
                CreatedByUserId = job.CreatedByUserId,
                ParentJobId = job.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonSerializer.Serialize(childPayload),
            });
        }

        job.TotalChildCount = latestVersions.Count;
        await db.SaveChangesAsync(ct);
        signal.Signal();
    }
}
