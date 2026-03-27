using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;

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

        var payload = new BulkReprocessPayload(request.Ids, request.TextExtractionEngineId);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.BulkReprocessDocuments, opts =>
        {
            opts.CreatedByUserId = currentUser.UserId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new BulkReprocessDocumentsResponse([jobId]);
    }
}
