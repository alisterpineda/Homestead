using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

public class BulkReprocessDocumentsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IDocumentQueryService queryService,
    ITextExtractionEngineResolver engineResolver,
    IJobQueue jobQueue,
    IValidator<BulkReprocessDocumentsCommand> validator)
{
    public async Task<BulkReprocessDocumentsResponse> Handle(BulkReprocessDocumentsCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        // Resolved before the empty-batch shortcut, so an unusable explicit pick is rejected the same way
        // whether or not the ids matched anything. Otherwise the same bad engine key would 400 with one
        // real id and return 200 with none — the request is wrong either way.
        // Still before the first write: reads do not open the command transaction.
        var engineKey = engineResolver.ResolveKey(request.EngineKey);

        var existingIds = await db.Documents
            .Where(d => request.Ids.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (existingIds.Count == 0)
            return new BulkReprocessDocumentsResponse([]);

        // All-or-nothing: if any document is inaccessible, reject the whole batch
        await queryService.EnsureAllDocumentsWritableAsync(
            db.Documents.Where(d => existingIds.Contains(d.Id)).Select(d => d.Id), ct);

        var payload = new BulkReprocessPayload(existingIds, engineKey);

        var jobId = await jobQueue.EnqueueAsync(ProcessingJobType.BulkReprocessDocuments, opts =>
        {
            opts.CreatedByUserId = currentUser.UserId;
            opts.Payload = System.Text.Json.JsonSerializer.Serialize(payload);
        }, ct);

        return new BulkReprocessDocumentsResponse([jobId]);
    }
}
