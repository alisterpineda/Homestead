using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Documents.BulkReprocessDocuments;

public class BulkReprocessDocumentsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IDocumentQueryService queryService,
    IJobQueue jobQueue)
    : IRequestHandler<BulkReprocessDocumentsCommand, BulkReprocessDocumentsResponse>
{
    public async Task<BulkReprocessDocumentsResponse> Handle(BulkReprocessDocumentsCommand request, CancellationToken ct)
    {
        // All-or-nothing: if any document is inaccessible, reject the whole batch
        var documentIds = db.Documents.Where(d => request.Ids.Contains(d.Id)).Select(d => d.Id);
        await queryService.EnsureAllDocumentsWritableAsync(documentIds, ct);

        if (request.TextExtractionEngineId is { } engineId)
        {
            await TextExtractionEngineValidator.ValidateEngineExistsAsync(db, engineId, ct);
        }

        var latestVersions = await db.DocumentVersions
            .Where(v => request.Ids.Contains(v.DocumentId))
            .GroupBy(v => v.DocumentId)
            .Select(g => new
            {
                DocumentId = g.Key,
                VersionId = g.OrderByDescending(v => v.VersionNumber).Select(v => v.Id).First(),
            })
            .ToListAsync(ct);

        var userId = currentUser.UserId;
        var jobIds = new List<Guid>(latestVersions.Count);

        foreach (var item in latestVersions)
        {
            var payload = new ReprocessDocumentVersionPayload(item.DocumentId, item.VersionId, request.TextExtractionEngineId);

            var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.ReprocessDocumentVersion, opts =>
            {
                opts.EntityType = ProcessingEntityType.Document;
                opts.EntityId = item.DocumentId;
                opts.CreatedByUserId = userId;
                opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
            }, ct);

            jobIds.Add(jobId);
        }

        return new BulkReprocessDocumentsResponse(jobIds);
    }
}
