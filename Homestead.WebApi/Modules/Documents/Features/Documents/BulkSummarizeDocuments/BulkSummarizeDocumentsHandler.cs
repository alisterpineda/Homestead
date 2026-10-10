using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkSummarizeDocuments;

public class BulkSummarizeDocumentsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IDocumentQueryService queryService,
    IJobQueue jobQueue)
{
    public async Task<BulkClassifyDocumentsResponse> Handle(BulkSummarizeDocumentsCommand request, CancellationToken ct)
    {
        var existingIds = await db.Documents
            .Where(d => request.Ids.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (existingIds.Count == 0)
            return new BulkClassifyDocumentsResponse([]);

        await queryService.EnsureAllDocumentsWritableAsync(
            db.Documents.Where(d => existingIds.Contains(d.Id)).Select(d => d.Id), ct);

        // Same parent job type as bulk classify, scoped to Summary only.
        var payload = new BulkClassifyPayload(existingIds, ClassificationScope.Summary);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.BulkClassifyDocuments, opts =>
        {
            opts.CreatedByUserId = currentUser.UserId;
            opts.Payload = JsonSerializer.Serialize(payload);
        }, ct);

        return new BulkClassifyDocumentsResponse([jobId]);
    }
}
