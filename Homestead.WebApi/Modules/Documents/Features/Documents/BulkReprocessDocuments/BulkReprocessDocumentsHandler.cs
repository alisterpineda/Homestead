using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

public class BulkReprocessDocumentsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IDocumentQueryService queryService,
    IJobQueue jobQueue)
    : IRequestHandler<BulkReprocessDocumentsCommand, BulkReprocessDocumentsResponse>
{
    public async Task<BulkReprocessDocumentsResponse> Handle(BulkReprocessDocumentsCommand request, CancellationToken ct)
    {
        var existingIds = await db.Documents
            .Where(d => request.Ids.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (existingIds.Count == 0)
            return new BulkReprocessDocumentsResponse([]);

        // All-or-nothing: if any document is inaccessible, reject the whole batch
        await queryService.EnsureAllDocumentsWritableAsync(
            db.Documents.Where(d => existingIds.Contains(d.Id)).Select(d => d.Id), ct);

        var payload = new BulkReprocessPayload(existingIds);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.BulkReprocessDocuments, opts =>
        {
            opts.CreatedByUserId = currentUser.UserId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new BulkReprocessDocumentsResponse([jobId]);
    }
}
